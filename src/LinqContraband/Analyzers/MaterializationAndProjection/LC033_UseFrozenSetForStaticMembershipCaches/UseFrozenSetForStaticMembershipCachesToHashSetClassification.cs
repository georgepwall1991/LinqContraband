using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

internal static partial class UseFrozenSetForStaticMembershipCachesAnalysis
{
    private static bool IsSupportedToHashSetInvocation(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        FrozenSetSupport support,
        CancellationToken cancellationToken)
    {
        if (invocation.TargetMethod.Name != "ToHashSet" ||
            !invocation.TargetMethod.IsExtensionMethod ||
            !invocation.TargetMethod.IsFrameworkMethod() ||
            !IsHashSetType(invocation.Type, support.HashSetType))
        {
            return false;
        }

        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax ||
            invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (invocation.Arguments.Length is not (1 or 2))
            return false;

        if (IsStaticTypeOrNamespaceAccess(memberAccess.Expression, semanticModel, cancellationToken))
            return false;

        var parameters = invocation.TargetMethod.Parameters;
        if (parameters.Length == 1)
            return IsEnumerableType(parameters[0].Type, invocation.Type is INamedTypeSymbol named ? named.TypeArguments[0] : null);

        return parameters.Length == 2 &&
               IsEnumerableType(parameters[0].Type, invocation.Type is INamedTypeSymbol type ? type.TypeArguments[0] : null) &&
               IsEqualityComparerType(parameters[1].Type, invocation.Type is INamedTypeSymbol comparerType ? comparerType.TypeArguments[0] : null);
    }

    private static bool IsStaticTypeOrNamespaceAccess(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken);
        if (IsTypeOrNamespaceSymbol(symbolInfo.Symbol))
            return true;

        return symbolInfo.CandidateSymbols.Any(IsTypeOrNamespaceSymbol);
    }

    private static bool IsTypeOrNamespaceSymbol(ISymbol? symbol)
    {
        return symbol switch
        {
            ITypeSymbol => true,
            INamespaceSymbol => true,
            IAliasSymbol { Target: ITypeSymbol or INamespaceSymbol } => true,
            _ => false
        };
    }
}
