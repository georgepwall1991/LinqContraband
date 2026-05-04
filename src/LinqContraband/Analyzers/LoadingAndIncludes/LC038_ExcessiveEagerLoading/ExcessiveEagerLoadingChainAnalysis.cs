using System;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC038_ExcessiveEagerLoading;

public sealed partial class ExcessiveEagerLoadingAnalyzer
{
    private static bool TryCountIncludeChain(IInvocationOperation outermostInvocation, out int includeCount)
    {
        includeCount = 0;

        IOperation? current = outermostInvocation;
        while (current is IInvocationOperation invocation && IsIncludeLike(invocation.TargetMethod))
        {
            includeCount++;
            current = invocation.GetInvocationReceiver();
        }

        if (current == null)
            return false;

        return IsProvableEfRoot(current);
    }

    private static bool IsProvableEfRoot(IOperation operation)
    {
        operation = operation.UnwrapConversions();

        return operation switch
        {
            IPropertyReferenceOperation propertyReference => propertyReference.Type.IsDbSet(),
            IFieldReferenceOperation fieldReference => fieldReference.Type.IsDbSet(),
            IInvocationOperation invocation => IsDbContextSetInvocation(invocation) ||
                                               IsTransparentQueryShaper(invocation) &&
                                               invocation.GetInvocationReceiver() is { } receiver &&
                                               IsProvableEfRoot(receiver),
            _ => false
        };
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext();
    }

    private static bool IsTransparentQueryShaper(IInvocationOperation invocation)
    {
        if (invocation.GetInvocationReceiverType()?.IsIQueryable() != true)
            return false;

        if (invocation.TargetMethod.ContainingNamespace?.ToString() == "System.Linq" &&
            invocation.TargetMethod.ContainingType.Name == "Queryable")
        {
            return invocation.TargetMethod.Name is
                "Where" or
                "OrderBy" or "OrderByDescending" or
                "ThenBy" or "ThenByDescending" or
                "Skip" or "Take";
        }

        if (invocation.TargetMethod.ContainingNamespace?.ToString()?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
        {
            return invocation.TargetMethod.Name is
                "AsNoTracking" or
                "AsNoTrackingWithIdentityResolution" or
                "AsTracking" or
                "AsSplitQuery" or
                "AsSingleQuery" or
                "TagWith";
        }

        return false;
    }

    private static bool IsIncludeLike(IMethodSymbol method)
    {
        return IncludeLikeMethods.Contains(method.Name) &&
               method.ContainingNamespace?.ToString()?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
    }
}
