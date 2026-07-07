using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC029_RedundantIdentitySelect;

public sealed partial class RedundantIdentitySelectAnalyzer
{
    private static IAnonymousFunctionOperation? TryGetLambda(IOperation operation, out bool isDelegateCreation)
    {
        isDelegateCreation = false;
        var value = operation.UnwrapConversions();
        if (value is IAnonymousFunctionOperation lambda)
        {
            return lambda;
        }

        if (value is IDelegateCreationOperation { Target: IAnonymousFunctionOperation delegateLambda })
        {
            isDelegateCreation = true;
            return delegateLambda;
        }

        return null;
    }

    private static bool IsExactEnumerableInterface(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.Name == "IEnumerable" &&
               namedType.ContainingNamespace?.ToString() == "System.Collections.Generic" &&
               namedType.TypeArguments.Length == 1;
    }

    private static bool IsTypePreservingSelector(IAnonymousFunctionOperation lambda)
    {
        var parameter = lambda.Symbol.Parameters.FirstOrDefault();
        return parameter != null &&
               SymbolEqualityComparer.Default.Equals(parameter.Type, lambda.Symbol.ReturnType);
    }

    private static bool IsIdentityLambda(IAnonymousFunctionOperation lambda)
    {
        var body = lambda.Body.Operations.FirstOrDefault();
        if (body is IReturnOperation returnOp) body = returnOp.ReturnedValue;
        if (body == null) return false;

        body = UnwrapIdentityValue(body);

        if (body is IParameterReferenceOperation paramRef)
        {
            return SymbolEqualityComparer.Default.Equals(paramRef.Parameter, lambda.Symbol.Parameters.FirstOrDefault());
        }

        return false;
    }

    private static IOperation UnwrapIdentityValue(IOperation operation)
    {
        var current = operation;
        while (true)
        {
            if (current is IConversionOperation { IsImplicit: true } conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            return current;
        }
    }
}
