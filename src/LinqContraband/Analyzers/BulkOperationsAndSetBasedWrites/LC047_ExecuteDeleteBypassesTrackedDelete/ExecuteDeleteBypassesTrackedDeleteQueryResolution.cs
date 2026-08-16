using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

public sealed partial class ExecuteDeleteBypassesTrackedDeleteAnalyzer
{
    internal static bool TryResolveDeleteTarget(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        out ITypeSymbol entityType,
        out ITypeSymbol contextType)
    {
        entityType = null!;
        contextType = null!;

        if (!TryGetEntityType(invocation, out entityType))
            return false;

        var receiver = invocation.GetInvocationReceiver();
        var executableRoot = invocation.FindOwningExecutableRoot();
        return TryResolveContextType(receiver, executableRoot, cancellationToken, out contextType);
    }

    internal static bool TryGetEntityType(IInvocationOperation invocation, out ITypeSymbol entityType)
    {
        entityType = null!;
        if (invocation.TargetMethod.TypeArguments.Length == 1)
        {
            entityType = invocation.TargetMethod.TypeArguments[0];
            return true;
        }

        var receiverType = invocation.GetInvocationReceiverType();
        if (receiverType is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            (named.IsIQueryable() || named.IsDbSet()))
        {
            entityType = named.TypeArguments[0];
            return true;
        }

        return false;
    }

    internal static bool TryResolveContextType(
        IOperation? source,
        IOperation? executableRoot,
        CancellationToken cancellationToken,
        out ITypeSymbol contextType)
    {
        contextType = null!;
        var current = source?.UnwrapConversions();

        for (var depth = 0; depth < 16 && current != null; depth++)
        {
            if (current.Type is { } type && type.IsDbContext() && !IsFrameworkDbContextType(type))
            {
                contextType = type;
                return true;
            }

            switch (current)
            {
                case ILocalReferenceOperation local when executableRoot != null:
                {
                    if (!LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                            executableRoot,
                            local.Local,
                            local.Syntax.SpanStart,
                            out var assigned,
                            cancellationToken))
                    {
                        return false;
                    }

                    current = assigned.UnwrapConversions();
                    continue;
                }
                case IParameterReferenceOperation parameter when parameter.Type is { } parameterType && parameterType.IsDbContext():
                    if (IsFrameworkDbContextType(parameterType))
                        return false;
                    contextType = parameterType;
                    return true;
                case IInvocationOperation invocation
                    when invocation.TargetMethod.Name == "Set" &&
                         invocation.Instance?.Type.IsDbContext() == true:
                    current = invocation.Instance.UnwrapConversions();
                    continue;
                case IInvocationOperation invocation
                    when TryGetTransparentQuerySource(invocation, out var invocationSource):
                    current = invocationSource.UnwrapConversions();
                    continue;
                case IMemberReferenceOperation member when member.Instance != null:
                    current = member.Instance.UnwrapConversions();
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    internal static bool TryGetTransparentQuerySource(IInvocationOperation invocation, out IOperation source)
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

        if (!IsSingleSourceTransparentQueryMethod(method.Name))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        if (namespaceName != "System.Linq" &&
            namespaceName != "Microsoft.EntityFrameworkCore" &&
            namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) != true)
        {
            return false;
        }

        var candidate = invocation.Arguments[0].Value.UnwrapConversions();
        if (!candidate.Type.IsIQueryable() && !candidate.Type.IsDbSet())
            return false;

        source = candidate;
        return true;
    }

    private static bool IsSingleSourceTransparentQueryMethod(string name)
    {
        return name is
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

    private static bool IsFrameworkDbContextType(ITypeSymbol? type)
    {
        return type?.Name == "DbContext" &&
               type.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }
}
