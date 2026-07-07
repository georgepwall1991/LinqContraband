using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static IEnumerable<INamedTypeSymbol> GetRegisteredTypes(IInvocationOperation invocation)
    {
        var typeArguments = invocation.TargetMethod.TypeArguments
            .OfType<INamedTypeSymbol>()
            .ToArray();

        if (typeArguments.Length > 0)
        {
            yield return typeArguments[typeArguments.Length - 1];

            if (typeArguments.Length == 1 || typeArguments[0].IsDbContext())
            {
                yield break;
            }

            yield return typeArguments[0];
            yield break;
        }

        var typeOfArguments = invocation.Arguments
            .Select(argument => GetTypeOfOperand(argument.Value))
            .OfType<INamedTypeSymbol>()
            .ToArray();

        if (typeOfArguments.Length == 0)
        {
            yield break;
        }

        yield return typeOfArguments[typeOfArguments.Length - 1];

        if (typeOfArguments.Length > 1 && typeOfArguments[0].IsDbContext())
        {
            yield return typeOfArguments[0];
        }
    }

    private static ITypeSymbol? GetTypeOfOperand(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        return operation is ITypeOfOperation typeOfOperation
            ? typeOfOperation.TypeOperand
            : null;
    }
}
