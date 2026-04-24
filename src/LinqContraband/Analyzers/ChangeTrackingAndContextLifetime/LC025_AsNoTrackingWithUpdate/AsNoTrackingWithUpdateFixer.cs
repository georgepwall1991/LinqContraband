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
using Microsoft.CodeAnalysis.Operations;

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

    private static InvocationExpressionSyntax? FindAsNoTrackingOrigin(
        SyntaxNode root,
        SemanticModel semanticModel,
        ILocalSymbol local,
        int boundary,
        CancellationToken cancellationToken)
    {
        var bestPosition = -1;
        InvocationExpressionSyntax? bestInvocation = null;

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer == null || declarator.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), local)) continue;

            UpdateBest(declarator.Initializer.Value, declarator.SpanStart);
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.SpanStart >= boundary) continue;

            var target = semanticModel.GetOperation(assignment.Left, cancellationToken)?.UnwrapConversions();
            if (target is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            UpdateBest(assignment.Right, assignment.SpanStart);
        }

        foreach (var forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (!forEach.Span.Contains(boundary) || forEach.Expression.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(forEach, cancellationToken), local)) continue;

            UpdateBest(forEach.Expression, forEach.Expression.SpanStart);
        }

        return bestInvocation;

        void UpdateBest(ExpressionSyntax expression, int position)
        {
            if (position < bestPosition) return;

            var invocation = FindAsNoTrackingInvocation(expression);
            bestPosition = position;
            bestInvocation = invocation;
        }
    }

    private static InvocationExpressionSyntax? FindAsNoTrackingInvocation(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "AsNoTracking" &&
                invocation.ArgumentList.Arguments.Count == 0);
    }
}
