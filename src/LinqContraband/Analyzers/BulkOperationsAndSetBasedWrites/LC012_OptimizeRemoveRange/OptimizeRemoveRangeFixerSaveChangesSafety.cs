using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeFixer
{
    private static bool HasSubsequentSaveChangesInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation removeRangeOperation)
            return HasSubsequentSaveChangesInvocationBySyntax(invocation, semanticModel, cancellationToken);

        var executableRoot = removeRangeOperation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return HasSubsequentSaveChangesInvocationBySyntax(invocation, semanticModel, cancellationToken);

        var removeRangeReceiver = GetRemoveRangeContextReceiver(removeRangeOperation);

        foreach (var candidate in executableRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (candidate.Syntax.SpanStart <= invocation.SpanStart ||
                !IsSaveChangesMethod(candidate.TargetMethod))
            {
                continue;
            }

            if (AreMutuallyExclusiveBranches(invocation, candidate.Syntax))
                continue;

            if (removeRangeReceiver != null &&
                TryResolveFreshContextLocal(removeRangeReceiver, executableRoot, cancellationToken, out var removeLocal) &&
                TryResolveFreshContextLocal(candidate.Instance, executableRoot, cancellationToken, out var saveLocal) &&
                !SymbolEqualityComparer.Default.Equals(removeLocal, saveLocal) &&
                TryResolveQuerySourceFreshContextLocal(removeRangeOperation.Arguments[0].Value, executableRoot, cancellationToken, out var queryLocal) &&
                SymbolEqualityComparer.Default.Equals(removeLocal, queryLocal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasSubsequentSaveChangesInvocationBySyntax(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var executableRoot = FindExecutableSyntaxRoot(invocation);
        if (executableRoot == null)
            return false;

        foreach (var subsequentInvocation in executableRoot
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>()
                     .Where(node => node.SpanStart > invocation.SpanStart))
        {
            if (semanticModel.GetSymbolInfo(subsequentInvocation, cancellationToken).Symbol is IMethodSymbol method &&
                IsSaveChangesMethod(method))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? FindExecutableSyntaxRoot(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or
                AccessorDeclarationSyntax or BaseMethodDeclarationSyntax)
            {
                return ancestor;
            }
        }

        return null;
    }

    private static bool IsSaveChangesMethod(IMethodSymbol method)
    {
        return (method.Name is "SaveChanges" or "SaveChangesAsync") &&
               method.ContainingType.IsDbContext();
    }
}
