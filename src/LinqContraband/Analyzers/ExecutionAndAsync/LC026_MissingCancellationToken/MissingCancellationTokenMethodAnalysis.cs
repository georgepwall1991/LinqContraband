using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC026_MissingCancellationToken;

public sealed partial class MissingCancellationTokenAnalyzer
{
    private bool IsCandidateAsyncEfMethod(IMethodSymbol method, out IParameterSymbol? cancellationTokenParameter)
    {
        cancellationTokenParameter = null;

        if (!method.Name.EndsWith("Async", System.StringComparison.Ordinal))
            return false;

        if (!IsEfCoreMethod(method))
            return false;

        cancellationTokenParameter = method.Parameters.FirstOrDefault(IsCancellationTokenParameter);
        return cancellationTokenParameter != null;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        return IsCancellationTokenType(parameter.Type);
    }

    private static IArgumentOperation? FindCancellationTokenArgument(
        IInvocationOperation invocation,
        IParameterSymbol cancellationTokenParameter)
    {
        return invocation.Arguments.FirstOrDefault(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter, cancellationTokenParameter));
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        var ns = method.ContainingNamespace?.ToString();
        return ns != null &&
               (ns.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) ||
                ns.StartsWith("System.Data.Entity", System.StringComparison.Ordinal));
    }

    private static bool IsCancellationTokenType(ITypeSymbol type)
    {
        return type.Name == "CancellationToken" &&
               type.ContainingNamespace?.ToString() == "System.Threading";
    }

    private bool IsUsingDefault(IOperation operation)
    {
        var unwrapped = operation.UnwrapConversions();
        return unwrapped.Kind == OperationKind.DefaultValue ||
               (unwrapped is IPropertyReferenceOperation propRef &&
                propRef.Property.Name == "None" &&
                propRef.Property.ContainingType.Name == "CancellationToken");
    }
}
