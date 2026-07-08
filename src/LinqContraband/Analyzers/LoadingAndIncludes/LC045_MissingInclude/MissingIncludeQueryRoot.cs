using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static bool TryGetDbSetRoot(
        IOperation? operation,
        out INamedTypeSymbol entityType,
        out INamedTypeSymbol contextType)
    {
        entityType = null!;
        contextType = null!;

        if (operation is IInvocationOperation invocation)
            return TryGetDbContextSetRoot(invocation, out entityType, out contextType);

        if (operation is not IMemberReferenceOperation memberReference)
            return false;
        if (operation is not (IPropertyReferenceOperation or IFieldReferenceOperation))
            return false;

        var rootType = memberReference.Type;
        if (rootType == null || !rootType.IsDbSet())
            return false;

        var contextCandidate = memberReference.Instance?.Type as INamedTypeSymbol ??
                               memberReference.Member.ContainingType;
        if (contextCandidate == null || !contextCandidate.IsDbContext())
            return false;

        if (rootType is not INamedTypeSymbol namedRoot ||
            namedRoot.TypeArguments.Length != 1 ||
            namedRoot.TypeArguments[0] is not INamedTypeSymbol element)
        {
            return false;
        }

        entityType = element;
        contextType = contextCandidate;
        return true;
    }

    private static bool IsDbContextSetRoot(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.Parameters.Length == 0 &&
               invocation.TargetMethod.ContainingType.IsDbContext() &&
               invocation.Type.IsDbSet();
    }

    private static bool TryGetDbContextSetRoot(
        IInvocationOperation invocation,
        out INamedTypeSymbol entityType,
        out INamedTypeSymbol contextType)
    {
        entityType = null!;
        contextType = null!;

        if (!IsDbContextSetRoot(invocation))
            return false;

        if (invocation.Type is not INamedTypeSymbol rootType ||
            rootType.TypeArguments.Length != 1 ||
            rootType.TypeArguments[0] is not INamedTypeSymbol element)
        {
            return false;
        }

        var contextCandidate = invocation.Instance?.Type as INamedTypeSymbol ??
                               invocation.TargetMethod.ContainingType;
        if (contextCandidate == null || !contextCandidate.IsDbContext())
            return false;

        entityType = element;
        contextType = contextCandidate;
        return true;
    }
}
