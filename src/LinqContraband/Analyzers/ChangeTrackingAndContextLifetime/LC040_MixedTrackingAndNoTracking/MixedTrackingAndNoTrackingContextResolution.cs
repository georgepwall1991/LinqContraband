using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool TryGetContextSymbol(IInvocationOperation invocation, IOperation root, out ISymbol? contextSymbol)
        {
            contextSymbol = null;

            var current = invocation.GetInvocationReceiver();
            while (current != null)
            {
                current = current.UnwrapConversions();

                if (current is IInvocationOperation nestedInvocation)
                {
                    if (nestedInvocation.TargetMethod.Name == "Select")
                        return false;

                    if (nestedInvocation.TargetMethod.Name == "Set" &&
                        nestedInvocation.TargetMethod.ContainingType.IsDbContext())
                        return TryGetSymbol(nestedInvocation.Instance, out contextSymbol);

                    current = nestedInvocation.GetInvocationReceiver();
                    continue;
                }

                switch (current)
                {
                    case IPropertyReferenceOperation propertyReference when propertyReference.Type.IsDbSet():
                        return TryGetSymbol(propertyReference.Instance, out contextSymbol);

                    case IFieldReferenceOperation fieldReference when fieldReference.Type.IsDbSet():
                        return TryGetSymbol(fieldReference.Instance, out contextSymbol);

                    case ILocalReferenceOperation localReference:
                        if (!TryResolveAssignedValue(localReference, root, out var assignedValue))
                            return false;

                        current = assignedValue;
                        continue;

                    case IParameterReferenceOperation:
                        return false;

                    default:
                        return false;
                }
            }

            return false;
        }

        private static bool TryGetSymbol(IOperation? operation, out ISymbol? symbol)
        {
            switch (operation?.UnwrapConversions())
            {
                case ILocalReferenceOperation localReference:
                    symbol = localReference.Local;
                    return true;
                case IParameterReferenceOperation parameterReference:
                    symbol = parameterReference.Parameter;
                    return true;
                case IFieldReferenceOperation fieldReference:
                    symbol = fieldReference.Field;
                    return true;
                case IPropertyReferenceOperation propertyReference:
                    symbol = propertyReference.Property;
                    return true;
                default:
                    symbol = null;
                    return false;
            }
        }
    }
}
