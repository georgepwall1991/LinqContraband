using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

internal static partial class NPlusOneLooperAnalysis
{
    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext();
    }

    private static bool IsClientBoundaryInvocation(IInvocationOperation invocation)
    {
        return ImmediateQueryExecutionMethods.Contains(invocation.TargetMethod.Name) ||
               SetBasedExecutorMethods.Contains(invocation.TargetMethod.Name);
    }

    private static bool IsNavigationQueryInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Query" &&
               IsChangeTrackingNamespace(invocation.TargetMethod.ContainingType.ContainingNamespace);
    }

    private static bool IsNavigationAccessInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name is "Reference" or "Collection" &&
               IsChangeTrackingNamespace(invocation.TargetMethod.ContainingType.ContainingNamespace);
    }

    private static bool IsAsQueryableInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "AsQueryable" &&
               invocation.TargetMethod.IsFrameworkMethod();
    }

    private static bool IsExplicitLoadReceiver(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return type.Name is "ReferenceEntry" or "CollectionEntry" &&
               IsChangeTrackingNamespace(type.ContainingNamespace);
    }

    private static bool IsChangeTrackingNamespace(INamespaceSymbol? ns)
    {
        return ns?.ToString() == "Microsoft.EntityFrameworkCore.ChangeTracking";
    }
}
