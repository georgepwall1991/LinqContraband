using System.Collections.Concurrent;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

public sealed partial class MissingAsNoTrackingAnalyzer
{
    private static bool HasWriteOperations(
        IOperation operation,
        ConcurrentDictionary<SyntaxNode, bool> writeOperationCache)
    {
        var root = operation.FindOwningExecutableRoot();
        if (root == null)
            return false;

        return writeOperationCache.GetOrAdd(root.Syntax, _ => ComputeHasWriteOperations(root));
    }

    private static bool ComputeHasWriteOperations(IOperation root)
    {
        foreach (var descendant in root.Descendants())
        {
            if (descendant is not IInvocationOperation inv)
                continue;

            if (inv.TargetMethod.Name == "SaveChanges" || inv.TargetMethod.Name == "SaveChangesAsync")
                return true;

            var name = inv.TargetMethod.Name;
            var receiverType = inv.Instance?.Type ?? (inv.Arguments.Length > 0 ? inv.Arguments[0].Value.Type : null);

            if ((name == "Add" || name == "AddAsync" ||
                 name == "Update" || name == "Remove" || name == "RemoveRange" || name == "AddRange" ||
                 name == "AddRangeAsync") &&
                (receiverType?.IsDbSet() == true || receiverType?.IsDbContext() == true))
            {
                return true;
            }
        }

        return false;
    }
}
