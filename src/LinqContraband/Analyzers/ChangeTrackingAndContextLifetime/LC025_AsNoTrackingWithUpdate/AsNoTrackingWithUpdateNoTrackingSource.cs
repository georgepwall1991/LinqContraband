using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateAnalyzer
{
    private bool IsNoTrackingSource(IOperation source, IOperation boundaryOperation, ISet<ISymbol> visited)
    {
        var unwrapped = source.UnwrapConversions();
        if (IsAsNoTrackingQuery(unwrapped)) return true;

        if (unwrapped is IInvocationOperation invocation &&
            invocation.TargetMethod.Name.IsMaterializerMethod() &&
            invocation.GetInvocationReceiver() is ILocalReferenceOperation receiverLocal)
        {
            return IsFromNoTrackingQuery(
                receiverLocal.Local,
                boundaryOperation,
                new HashSet<ISymbol>(visited, SymbolEqualityComparer.Default));
        }

        return unwrapped is ILocalReferenceOperation localRef &&
               IsFromNoTrackingQuery(
                   localRef.Local,
                   boundaryOperation,
                   new HashSet<ISymbol>(visited, SymbolEqualityComparer.Default));
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        yield return root;

        foreach (var child in root.ChildOperations)
        {
            foreach (var descendant in EnumerateOperations(child))
                yield return descendant;
        }
    }
}
