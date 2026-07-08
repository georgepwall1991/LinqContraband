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
        var factoryReturnTypes = invocation.Arguments
            .Select(argument => GetFactoryReturnType(argument.Value))
            .OfType<INamedTypeSymbol>()
            .ToArray();

        if (typeArguments.Length > 0)
        {
            foreach (var factoryReturnType in factoryReturnTypes)
            {
                yield return factoryReturnType;
            }

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

    private static ITypeSymbol? GetFactoryReturnType(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        if (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = UnwrapConversion(delegateCreation.Target);
        }

        if (operation is not IAnonymousFunctionOperation anonymousFunction)
        {
            return null;
        }

        foreach (var bodyOperation in anonymousFunction.Body.Operations)
        {
            var returnType = GetFactoryReturnTypeFromOperation(bodyOperation);
            if (returnType != null)
            {
                return returnType;
            }
        }

        return null;
    }

    private static ITypeSymbol? GetFactoryReturnTypeFromOperation(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        return operation switch
        {
            IReturnOperation { ReturnedValue: { } returnedValue } => GetFactoryReturnTypeFromValue(returnedValue),
            IExpressionStatementOperation { Operation: { } expression } => GetFactoryReturnTypeFromValue(expression),
            _ => null
        };
    }

    private static ITypeSymbol? GetFactoryReturnTypeFromValue(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        return operation is IObjectCreationOperation objectCreation
            ? objectCreation.Type
            : null;
    }
}
