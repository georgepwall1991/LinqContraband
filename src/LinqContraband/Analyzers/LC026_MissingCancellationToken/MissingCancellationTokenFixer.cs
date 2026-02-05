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

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

/// <summary>
/// Provides code fixes for LC026. Passes available CancellationToken to async methods.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCancellationTokenFixer))]
[Shared]
public class MissingCancellationTokenFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingCancellationTokenAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        // Try to find a cancellation token in scope
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        var cancellationTokenName = FindCancellationTokenInScope(semanticModel, invocation.SpanStart);

        if (cancellationTokenName != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Pass '{cancellationTokenName}'",
                    c => ApplyFixAsync(context.Document, invocation, cancellationTokenName, c),
                    "PassCancellationToken"),
                diagnostic);
        }
    }

    private string? FindCancellationTokenInScope(SemanticModel semanticModel, int position)
    {
        var symbols = semanticModel.LookupSymbols(position);

        // Prioritize parameters/locals named 'cancellationToken' or 'ct'
        var tokenSymbols = symbols.Where(s =>
            (s is ILocalSymbol l && l.Type.Name == "CancellationToken") ||
            (s is IParameterSymbol p && p.Type.Name == "CancellationToken")
        ).ToList();

        if (!tokenSymbols.Any()) return null;

        var preferred = tokenSymbols.FirstOrDefault(s => s.Name == "cancellationToken") ??
                        tokenSymbols.FirstOrDefault(s => s.Name == "ct") ??
                        tokenSymbols.First();

        return preferred.Name;
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, string tokenName, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var newArgument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName));
        var newArgumentList = invocation.ArgumentList.AddArguments(newArgument);
        var newInvocation = invocation.WithArgumentList(newArgumentList);

        editor.ReplaceNode(invocation, newInvocation);

        return editor.GetChangedDocument();
    }
}
