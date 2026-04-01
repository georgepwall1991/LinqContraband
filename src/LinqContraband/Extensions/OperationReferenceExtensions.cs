using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

public static partial class AnalysisExtensions
{
    public static bool ReferencesParameter(this IOperation operation, IParameterSymbol parameter)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is IParameterReferenceOperation parameterReference &&
            SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child.ReferencesParameter(parameter))
                return true;
        }

        return false;
    }

    public static bool ReferencesLocal(this IOperation operation, ILocalSymbol local)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is ILocalReferenceOperation localReference &&
            SymbolEqualityComparer.Default.Equals(localReference.Local, local))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child.ReferencesLocal(local))
                return true;
        }

        return false;
    }
}
