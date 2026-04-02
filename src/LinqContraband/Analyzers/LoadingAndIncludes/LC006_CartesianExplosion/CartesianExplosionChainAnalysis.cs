using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
{
    private static IncludeChainAnalysis AnalyzeIncludeChain(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel)
    {
        var result = new IncludeChainAnalysis();
        InvocationExpressionSyntax? current = invocationSyntax;

        while (current?.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var symbol = semanticModel.GetSymbolInfo(current).Symbol as IMethodSymbol;
            if (symbol?.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
            {
                current = memberAccess.Expression as InvocationExpressionSyntax;
                continue;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName == "AsSplitQuery")
            {
                result.HasSplitQuery = true;
            }
            else if (methodName == "Include" &&
                     TryGetIncludedNavigation(current, semanticModel, out var navigation))
            {
                result.CollectionIncludes.Add(navigation);
            }

            current = memberAccess.Expression as InvocationExpressionSyntax;
        }

        result.CollectionIncludes.Reverse();
        return result;
    }

    private static bool HasSplitQueryDownstream(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var current = invocation.Parent;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax parentInvocation &&
                parentInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "AsSplitQuery" &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol method &&
                method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
