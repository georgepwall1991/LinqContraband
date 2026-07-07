using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC019_ConditionalInclude;

public sealed partial class ConditionalIncludeAnalyzer
{
    private static bool HasConditionalIncludePath(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        return current switch
        {
            IConditionalOperation or ICoalesceOperation => true,
            IPropertyReferenceOperation property => property.Instance != null &&
                                                    HasConditionalIncludePath(property.Instance),
            IFieldReferenceOperation field => field.Instance != null &&
                                              HasConditionalIncludePath(field.Instance),
            IInvocationOperation invocation => HasConditionalIncludeInvocationSource(invocation),
            _ => false
        };
    }

    private static bool HasConditionalIncludeInvocationSource(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver();
        return receiver != null && HasConditionalIncludePath(receiver);
    }
}
