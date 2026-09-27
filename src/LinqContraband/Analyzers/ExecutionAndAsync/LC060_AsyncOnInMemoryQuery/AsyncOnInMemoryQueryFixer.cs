using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery;

/// <summary>
/// Provides code fixes for LC060. Runs the in-memory query synchronously: <c>await q.ToListAsync(ct)</c> becomes
/// <c>q.ToList()</c>, dropping the <c>await</c>, any <c>ConfigureAwait</c> and the cancellation token.
/// </summary>
/// <remarks>
/// Only an operator that is awaited where it is called, written as an extension call with positional arguments, and
/// that has a LINQ twin with the same arguments (<c>ToList</c>, <c>FirstOrDefault</c>, <c>CountAsync</c>, ...) gets a
/// fix. <c>ForEachAsync</c>, <c>LoadAsync</c> and <c>AsAsyncEnumerable</c> have no such twin. The fixer compiles the
/// rewritten document and offers nothing when the rewrite adds an error, or leaves an async function without an
/// <c>await</c> (CS1998).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncOnInMemoryQueryFixer))]
[Shared]
public sealed class AsyncOnInMemoryQueryFixer : CodeFixProvider
{
    private const string Title = "Run the in-memory query synchronously";
    private const string AsyncMethodWithoutAwait = "CS1998";

    private static readonly ImmutableHashSet<string> SynchronousTwins = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "ToList", "ToArray", "ToHashSet", "ToDictionary",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "ElementAt", "ElementAtOrDefault",
        "Count", "LongCount", "Any", "All", "Contains",
        "Sum", "Min", "Max", "Average");

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncOnInMemoryQueryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? problemsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var newRoot = Rewrite(root, semanticModel, diagnostic, cancellationToken);
            if (newRoot == null) continue;

            var newDocument = document.WithSyntaxRoot(newRoot);
            problemsBefore ??= CountProblems(semanticModel, cancellationToken);
            var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (newModel == null || CountProblems(newModel, cancellationToken) > problemsBefore.Value) continue;

            context.RegisterCodeFix(
                CodeAction.Create(Title, _ => Task.FromResult(newDocument), nameof(AsyncOnInMemoryQueryFixer)),
                diagnostic);
        }
    }

    private static SyntaxNode? Rewrite(
        SyntaxNode root,
        SemanticModel semanticModel,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var nameToken = root.FindToken(diagnostic.Location.SourceSpan.Start);
        if (nameToken.Parent is not SimpleNameSyntax name ||
            name.Parent is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name != name ||
            memberAccess.Parent is not InvocationExpressionSyntax invocation ||
            invocation.Expression != memberAccess)
        {
            return null;
        }

        var asyncName = name.Identifier.ValueText;
        if (!asyncName.EndsWith("Async", System.StringComparison.Ordinal))
            return null;

        var syncName = asyncName.Substring(0, asyncName.Length - "Async".Length);
        if (!SynchronousTwins.Contains(syncName))
            return null;

        // Called as an extension method, so the arguments line up with the reduced method's parameters.
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { ReducedFrom: not null } method)
            return null;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count > method.Parameters.Length || arguments.Any(a => a.NameColon != null || !a.RefKindKeyword.IsKind(SyntaxKind.None)))
            return null;

        var awaited = (ExpressionSyntax)invocation;
        if (awaited.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configureAwait &&
            configureAwait.Expression == awaited &&
            configureAwait.Parent is InvocationExpressionSyntax configureAwaitCall &&
            configureAwaitCall.Expression == configureAwait)
        {
            awaited = configureAwaitCall;
        }

        if (awaited.Parent is not AwaitExpressionSyntax awaitExpression)
            return null;

        // The cancellation token is the last parameter of every async operator: drop it, keep the rest as written.
        var keptCount = arguments.Count;
        if (keptCount > 0 && IsCancellationToken(method.Parameters[keptCount - 1].Type))
            keptCount--;

        if (Enumerable.Range(0, keptCount).Any(index => IsCancellationToken(method.Parameters[index].Type)))
            return null;

        var newArguments = keptCount == 0
            ? SyntaxFactory.SeparatedList<ArgumentSyntax>()
            : SyntaxFactory.SeparatedList<ArgumentSyntax>(
                arguments.GetWithSeparators().Take(keptCount * 2 - 1));
        if (keptCount > 0)
            newArguments = newArguments.Replace(newArguments.Last(), newArguments.Last().WithoutTrailingTrivia());

        var syncCall = invocation
            .WithExpression(memberAccess.WithName(
                SyntaxFactory.IdentifierName(syncName).WithTriviaFrom(name)))
            .WithArgumentList(invocation.ArgumentList.WithArguments(newArguments))
            .WithLeadingTrivia(awaitExpression.GetLeadingTrivia())
            .WithTrailingTrivia(awaitExpression.GetTrailingTrivia());

        return root.ReplaceNode(awaitExpression, syncCall);
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static int CountProblems(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Id == AsyncMethodWithoutAwait);
    }
}
