using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

/// <summary>
/// Provides code fixes for LC005. Replaces subsequent OrderBy calls with ThenBy/ThenByDescending for correct multi-level sorting.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MultipleOrderByFixer))]
[Shared]
public sealed class MultipleOrderByFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MultipleOrderByAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null) continue;
            if (!CanRewriteToThenBy(invocation, semanticModel)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Replace with ThenBy/ThenByDescending",
                    c => ReplaceWithThenByAsync(context.Document, invocation, c),
                    nameof(MultipleOrderByFixer)),
                diagnostic);
        }
    }

    private async Task<Document> ReplaceWithThenByAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var methodName = memberAccess.Name.Identifier.Text;
        var newMethodName = methodName == "OrderBy" ? "ThenBy" : "ThenByDescending";

        var newName = CreateReplacementName(memberAccess.Name, newMethodName);
        var newMemberAccess = memberAccess.WithName(newName);
        var newInvocation = invocation.WithExpression(newMemberAccess);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }

    private static SimpleNameSyntax CreateReplacementName(SimpleNameSyntax originalName, string newMethodName)
    {
        var identifier = SyntaxFactory.Identifier(
            originalName.Identifier.LeadingTrivia,
            newMethodName,
            originalName.Identifier.TrailingTrivia);

        return originalName switch
        {
            GenericNameSyntax genericName => genericName.WithIdentifier(identifier),
            IdentifierNameSyntax identifierName => identifierName.WithIdentifier(identifier),
            _ => SyntaxFactory.IdentifierName(identifier).WithTriviaFrom(originalName)
        };
    }

    private static bool CanRewriteToThenBy(InvocationExpressionSyntax invocation, SemanticModel? semanticModel)
    {
        if (semanticModel == null)
            return false;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var receiverExpression = GetLogicalReceiverExpression(invocation, memberAccess, semanticModel);
        if (receiverExpression == null)
            return false;

        var receiverType = semanticModel.GetTypeInfo(receiverExpression).Type;
        return IsOrderedSequence(receiverType);
    }

    private static ExpressionSyntax? GetLogicalReceiverExpression(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is INamedTypeSymbol type &&
            type.ContainingNamespace?.ToString() == "System.Linq" &&
            type.Name is "Enumerable" or "Queryable")
        {
            return invocation.ArgumentList.Arguments.Count > 0
                ? invocation.ArgumentList.Arguments[0].Expression
                : null;
        }

        return memberAccess.Expression;
    }

    private static bool IsOrderedSequence(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (IsOrderedSequenceType(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (IsOrderedSequenceType(iface))
                return true;
        }

        return false;
    }

    private static bool IsOrderedSequenceType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
            return false;

        var ns = namedType.ContainingNamespace?.ToString();
        return ns == "System.Linq" && namedType.Name is "IOrderedEnumerable" or "IOrderedQueryable";
    }
}
