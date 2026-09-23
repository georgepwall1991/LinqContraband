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
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC051_ToAsyncEnumerableOnQuery;

/// <summary>
/// Provides code fixes for LC051. Replaces <c>query.ToAsyncEnumerable()</c> with EF Core's
/// <c>query.AsAsyncEnumerable()</c>, which returns the same <c>IAsyncEnumerable&lt;T&gt;</c>. The static invocation form
/// (<c>AsyncEnumerable.ToAsyncEnumerable(query)</c>) is reported without a fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToAsyncEnumerableOnQueryFixer))]
[Shared]
public sealed class ToAsyncEnumerableOnQueryFixer : CodeFixProvider
{
    private const string Title = "Use AsAsyncEnumerable()";
    private const string EfCoreNamespace = "Microsoft.EntityFrameworkCore";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ToAsyncEnumerableOnQueryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        var extensions = semanticModel.Compilation.GetTypeByMetadataName(EfCoreNamespace + ".EntityFrameworkQueryableExtensions");
        if (extensions == null || !extensions.GetMembers("AsAsyncEnumerable").Any()) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is not IdentifierNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } name ||
                memberAccess.Name != name ||
                memberAccess.Parent is not InvocationExpressionSyntax invocation ||
                invocation.Expression != memberAccess ||
                invocation.ArgumentList.Arguments.Count != 0)
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol
                {
                    MethodKind: MethodKind.ReducedExtension
                })
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    ct => ReplaceAsync(context.Document, memberAccess, name, ct),
                    nameof(ToAsyncEnumerableOnQueryFixer)),
                diagnostic);
        }
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        MemberAccessExpressionSyntax memberAccess,
        IdentifierNameSyntax name,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(name, SyntaxFactory.IdentifierName("AsAsyncEnumerable").WithTriviaFrom(name));

        if (!IsAsAsyncEnumerableInScope(editor.SemanticModel, memberAccess))
            editor.EnsureUsing(EfCoreNamespace);

        return editor.GetChangedDocument();
    }

    private static bool IsAsAsyncEnumerableInScope(SemanticModel semanticModel, MemberAccessExpressionSyntax memberAccess)
    {
        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType == null)
            return false;

        return semanticModel
            .LookupSymbols(memberAccess.SpanStart, receiverType, "AsAsyncEnumerable", includeReducedExtensionMethods: true)
            .Any();
    }
}
