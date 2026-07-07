using System;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

public sealed partial class OptimizeRemoveRangeFixer
{
    private static bool TryResolveQuerySourceFreshContextLocal(
        IOperation? querySource,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out ILocalSymbol? creationLocal)
    {
        creationLocal = null;
        var current = querySource?.UnwrapConversions();

        for (var depth = 0; depth < 16; depth++)
        {
            switch (current)
            {
                case ILocalReferenceOperation localReference:
                {
                    var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);
                    if (assignments.Count != 1)
                        return false;

                    current = assignments[0].Value.UnwrapConversions();
                    continue;
                }

                case IInvocationOperation invocation
                    when TryGetTransparentQueryInvocationSource(invocation, out var invocationSource):
                    current = invocationSource.UnwrapConversions();
                    continue;

                case IInvocationOperation:
                    return false;

                case IMemberReferenceOperation memberReference:
                    return TryResolveFreshContextLocal(memberReference.Instance, executableRoot, cancellationToken, out creationLocal);

                default:
                    return TryResolveFreshContextLocal(current, executableRoot, cancellationToken, out creationLocal);
            }
        }

        return false;
    }

    private static bool TryGetTransparentQueryInvocationSource(
        IInvocationOperation invocation,
        out IOperation source)
    {
        source = null!;
        var method = invocation.TargetMethod;

        if (method.Name == "Set" && method.ContainingType.IsDbContext() && invocation.Instance != null)
        {
            source = invocation.Instance;
            return true;
        }

        if (!method.IsExtensionMethod || invocation.Arguments.Length == 0)
            return false;

        if (!IsSingleSourceTransparentQueryMethod(method))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        if (namespaceName != "System.Linq" &&
            namespaceName != "Microsoft.EntityFrameworkCore" &&
            namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) != true)
        {
            return false;
        }

        var candidate = invocation.Arguments[0].Value.UnwrapConversions();
        if (!candidate.Type.IsIQueryable() && !candidate.Type.IsDbSet())
            return false;

        source = candidate;
        return true;
    }

    private static bool IsSingleSourceTransparentQueryMethod(IMethodSymbol method)
    {
        return method.Name is
            "Where" or
            "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or
            "Skip" or "Take" or
            "Distinct" or "Reverse" or
            "AsQueryable" or
            "AsNoTracking" or "AsNoTrackingWithIdentityResolution" or "AsTracking" or
            "AsSplitQuery" or "AsSingleQuery" or
            "TagWith" or "IgnoreQueryFilters" or
            "Include" or "ThenInclude";
    }
}
