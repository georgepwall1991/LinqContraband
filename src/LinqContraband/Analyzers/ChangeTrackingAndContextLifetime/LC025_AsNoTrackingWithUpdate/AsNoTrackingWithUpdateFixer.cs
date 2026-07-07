using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Provides code fixes for LC025. Removes AsNoTracking from the source query.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsNoTrackingWithUpdateFixer))]
[Shared]
public sealed partial class AsNoTrackingWithUpdateFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsNoTrackingWithUpdateAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var argument = token.Parent.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
        if (argument == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        var local = GetLocalArgumentSymbol(semanticModel, argument, context.CancellationToken);
        if (local == null) return;

        if (FindAsNoTrackingOrigin(root, semanticModel, local, argument.SpanStart, context.CancellationToken) == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove AsNoTracking from query",
                c => ApplyFixAsync(context.Document, argument, c),
                "RemoveAsNoTracking"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var local = GetLocalArgumentSymbol(semanticModel, argument, cancellationToken);
        if (local == null) return document;

        var asNoTrackingInvocation = FindAsNoTrackingOrigin(root, semanticModel, local, argument.SpanStart, cancellationToken);
        if (asNoTrackingInvocation?.Expression is not MemberAccessExpressionSyntax asNoTrackingAccess) return document;

        if (asNoTrackingAccess.Expression is ExpressionSyntax source)
        {
            editor.ReplaceNode(asNoTrackingInvocation, source.WithTriviaFrom(asNoTrackingInvocation));
        }

        return editor.GetChangedDocument();
    }

    private static ILocalSymbol? GetLocalArgumentSymbol(SemanticModel semanticModel, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        var operation = semanticModel.GetOperation(argument.Expression, cancellationToken)?.UnwrapConversions();
        if (operation is ILocalReferenceOperation localReference)
            return localReference.Local;

        return semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol as ILocalSymbol;
    }

}
