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

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Provides code fixes for LC025. Removes AsNoTracking from the source query.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsNoTrackingWithUpdateFixer))]
[Shared]
public class AsNoTrackingWithUpdateFixer : CodeFixProvider
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

        // 1. Identify the variable being passed
        var symbol = semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol as ILocalSymbol;
        if (symbol == null) return document;

        // 2. Find the assignment to this variable
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var assignment = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(d => d.Identifier.Text == symbol.Name && d.Initializer != null);

        if (assignment?.Initializer?.Value is InvocationExpressionSyntax queryExpression)
        {
            // 3. Find .AsNoTracking() in the chain
            var asNoTrackingCall = queryExpression.DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .FirstOrDefault(m => m.Name.Identifier.Text == "AsNoTracking");

            if (asNoTrackingCall?.Parent is InvocationExpressionSyntax invocation)
            {
                // Remove the call from the chain: query.AsNoTracking().ToList() -> query.ToList()
                if (asNoTrackingCall.Expression is ExpressionSyntax source)
                {
                    // If it's a chained call, we need to replace the AsNoTracking invocation with its source
                    editor.ReplaceNode(invocation, source.WithTriviaFrom(invocation));
                }
            }
        }

        return editor.GetChangedDocument();
    }
}
