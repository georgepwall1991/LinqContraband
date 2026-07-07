using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool IsBlockAncestor(IBlockOperation ancestor, IBlockOperation descendant)
    {
        var current = descendant.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current.Parent;
        }

        return false;
    }

    private static IOperation? FindDirectChildOperationContainingSpan(
        ImmutableArray<IOperation> operations,
        TextSpan span)
    {
        IOperation? best = null;
        foreach (var operation in operations)
        {
            if (!operation.Syntax.Span.Contains(span.Start)) continue;
            if (best == null || operation.Syntax.Span.Length < best.Syntax.Span.Length)
                best = operation;
        }

        return best;
    }

    private static IBlockOperation? FindEnclosingBlock(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IBlockOperation block) return block;
            current = current.Parent;
        }

        return null;
    }
}
