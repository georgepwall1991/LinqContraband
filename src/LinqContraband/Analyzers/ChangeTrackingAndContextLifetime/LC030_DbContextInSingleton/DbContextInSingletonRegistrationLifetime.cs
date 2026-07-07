using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool HasSingletonContextLifetimeArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "contextLifetime" &&
                IsSingletonLifetime(argument.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSingletonLifetime(IOperation operation)
    {
        operation = UnwrapConversion(operation);
        if (operation is not IFieldReferenceOperation fieldReference)
        {
            return false;
        }

        return fieldReference.Field.Name == "Singleton" &&
               fieldReference.Field.ContainingType.Name == "ServiceLifetime" &&
               fieldReference.Field.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }
}
