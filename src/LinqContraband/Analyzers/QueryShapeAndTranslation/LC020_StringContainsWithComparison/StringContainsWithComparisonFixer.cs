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

namespace LinqContraband.Analyzers.LC020_StringContainsWithComparison;

/// <summary>
/// Provides code fixes for LC020. Removes StringComparison argument from string methods in LINQ queries.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringContainsWithComparisonFixer))]
[Shared]
public class StringContainsWithComparisonFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(StringContainsWithComparisonAnalyzer.DiagnosticId);

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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove StringComparison argument",
                c => ApplyFixAsync(context.Document, invocation, c),
                "RemoveStringComparison"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return document;

        var argumentToRemove = FindStringComparisonArgument(invocation, semanticModel, cancellationToken);
        if (argumentToRemove is null) return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var newArguments = invocation.ArgumentList.Arguments.Remove(argumentToRemove);
        var newArgumentList = invocation.ArgumentList.WithArguments(newArguments);
        var newInvocation = invocation.WithArgumentList(newArgumentList);

        editor.ReplaceNode(invocation, newInvocation);

        return editor.GetChangedDocument();
    }

    private static ArgumentSyntax? FindStringComparisonArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var parameterType = semanticModel.GetOperation(argument, cancellationToken) is IArgumentOperation argumentOperation
                ? argumentOperation.Parameter?.Type
                : null;
            var expressionType = semanticModel.GetTypeInfo(argument.Expression, cancellationToken).Type;

            if (IsStringComparison(parameterType) || IsStringComparison(expressionType))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool IsStringComparison(ITypeSymbol? type)
    {
        return type?.Name == nameof(System.StringComparison) &&
               type.ContainingNamespace?.ToString() == "System";
    }
}
