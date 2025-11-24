using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC001_LocalMethod;

/// <summary>
/// Provides code fixes for LC001. Switches LINQ queries to client-side evaluation using AsEnumerable() when local methods are called.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalMethodFixer))]
[Shared]
public class LocalMethodFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalMethodAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Switch to client-side evaluation (AsEnumerable)",
                c => SwitchToClientSideAsync(context.Document, invocation, c),
                "SwitchToClientSide"),
            diagnostic);
    }

    private async Task<Document> SwitchToClientSideAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        EnsureUsing(editor, "System.Linq");

        // 1. Find the Lambda containing the local method call
        var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda == null) return document;

        // 2. Find the LINQ operator invocation (e.g. .Where(), .Select()) that uses this lambda
        var queryInvocation = lambda.Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (queryInvocation == null) return document;

        // 3. Check if it is using extension method syntax: source.Where(...)
        if (queryInvocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var source = memberAccess.Expression;

        if (IsInvocationOf(source, "AsEnumerable")) return editor.GetChangedDocument();

        // 4. Create .AsEnumerable() call on the source
        // construct: source.AsEnumerable()
        var asEnumerable = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source,
            SyntaxFactory.IdentifierName("AsEnumerable"));

        var asEnumerableInvocation = SyntaxFactory.InvocationExpression(asEnumerable);

        // 5. Replace the original source with the new source, preserving trivia
        editor.ReplaceNode(source, asEnumerableInvocation.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }

    private static void EnsureUsing(DocumentEditor editor, string namespaceName)
    {
        var root = editor.OriginalRoot as CompilationUnitSyntax;
        if (root == null) return;
        if (root.Usings.Any(u =>
                u.Name?.ToString() == namespaceName ||
                (u.Alias != null && u.Name?.ToString() == namespaceName)))
            return;

        var newRoot = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName)));
        editor.ReplaceNode(root, newRoot);
    }
}
