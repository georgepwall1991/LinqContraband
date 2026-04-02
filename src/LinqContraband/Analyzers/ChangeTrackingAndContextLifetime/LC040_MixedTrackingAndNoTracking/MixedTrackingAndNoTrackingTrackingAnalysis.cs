using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

public sealed partial class MixedTrackingAndNoTrackingAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool TryGetTrackingMode(IInvocationOperation invocation, IOperation root, out TrackingMode mode)
        {
            mode = TrackingMode.Tracked;

            IOperation? current = invocation.GetInvocationReceiver();
            while (current != null)
            {
                current = current.UnwrapConversions();

                switch (current)
                {
                    case IInvocationOperation nestedInvocation:
                        if (nestedInvocation.TargetMethod.Name is "AsTracking")
                        {
                            mode = TrackingMode.Tracked;
                            return true;
                        }

                        if (nestedInvocation.TargetMethod.Name is "AsNoTracking" or "AsNoTrackingWithIdentityResolution")
                        {
                            mode = TrackingMode.NoTracking;
                            return true;
                        }

                        if (nestedInvocation.TargetMethod.Name == "Select")
                            return false;

                        current = nestedInvocation.GetInvocationReceiver();
                        continue;

                    case ILocalReferenceOperation localReference:
                        if (!TryResolveAssignedValue(localReference.Local, root, out var assignedValue))
                            return false;

                        current = assignedValue;
                        continue;

                    case IPropertyReferenceOperation:
                    case IFieldReferenceOperation:
                        return true;

                    case IParameterReferenceOperation:
                        return false;

                    default:
                        return true;
                }
            }

            return true;
        }
    }
}
