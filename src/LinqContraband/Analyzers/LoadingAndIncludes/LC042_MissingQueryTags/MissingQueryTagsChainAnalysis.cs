using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

public sealed partial class MissingQueryTagsAnalyzer
{
    private static bool TryAnalyzeChain(IOperation receiver, out int count)
    {
        count = 0;
        var current = receiver;

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                var methodName = invocation.TargetMethod.Name;

                if (methodName is "TagWith" or "TagWithCallSite")
                    return false;

                if (QuerySteps.Contains(methodName))
                {
                    count++;
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                if (methodName == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                    return true;

                return false;
            }

            if (IsDbSetSource(current))
                return true;

            if (current is IPropertyReferenceOperation or IFieldReferenceOperation or ILocalReferenceOperation or IParameterReferenceOperation)
                return false;

            return false;
        }

        return false;
    }

    private static bool IsDbSetSource(IOperation operation)
    {
        if (operation.Type != null && operation.Type.IsDbSet())
            return true;

        return operation switch
        {
            IPropertyReferenceOperation propertyReference when propertyReference.Type.IsDbSet() => true,
            IFieldReferenceOperation fieldReference when fieldReference.Type.IsDbSet() => true,
            ILocalReferenceOperation localReference when localReference.Type.IsDbSet() => true,
            IParameterReferenceOperation parameterReference when parameterReference.Type.IsDbSet() => true,
            _ => false
        };
    }
}
