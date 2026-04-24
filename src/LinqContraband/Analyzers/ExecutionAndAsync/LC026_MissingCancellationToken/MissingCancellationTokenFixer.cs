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
using Microsoft.CodeAnalysis.Operations;

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

        var cancellationTokenName = MissingCancellationTokenAnalyzer.FindCancellationTokenInScope(semanticModel, invocation.SpanStart);

        if (cancellationTokenName != null)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Pass '{cancellationTokenName}'",
                    c => ApplyFixAsync(context.Document, invocation, semanticModel, cancellationTokenName, c),
                    "PassCancellationToken"),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string tokenName,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var newArgument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(tokenName));
        var tokenArgument = FindExplicitCancellationTokenArgument(semanticModel, invocation, cancellationToken);
        var newInvocation = tokenArgument is null
            ? invocation.WithArgumentList(invocation.ArgumentList.AddArguments(newArgument))
            : invocation.ReplaceNode(tokenArgument, tokenArgument.WithExpression(SyntaxFactory.IdentifierName(tokenName)));

        editor.ReplaceNode(invocation, newInvocation);

        return editor.GetChangedDocument();
    }

    private static ArgumentSyntax? FindExplicitCancellationTokenArgument(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return null;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is null ||
                !IsCancellationTokenParameter(argument.Parameter) ||
                argument.Syntax is not ArgumentSyntax syntax)
            {
                continue;
            }

            return syntax;
        }

        return null;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        var type = parameter.Type;
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }
}
