using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

public sealed partial class DisposedContextQueryAnalyzer
{
    private static bool TryGetSingleAssignedValue(
        ILocalSymbol local,
        int position,
        IOperation executableRoot,
        out IOperation value)
    {
        var assignments = GetAssignedValues(local, position, executableRoot);
        value = null!;

        if (assignments.Count != 1)
            return false;

        value = assignments[0];
        return true;
    }

    private static List<IOperation> GetAssignedValues(
        ILocalSymbol local,
        int position,
        IOperation executableRoot)
    {
        var assignments = new List<(int Position, IOperation Value)>();

        foreach (var operation in EnumerateOperations(executableRoot))
        {
            if (operation.Syntax.SpanStart >= position)
                continue;

            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null:
                    assignments.Add((declarator.Syntax.SpanStart, declarator.Initializer.Value.UnwrapConversions()));
                    break;

                case ISimpleAssignmentOperation assignment
                    when IsLocalTarget(assignment.Target, local):
                    assignments.Add((assignment.Syntax.SpanStart, assignment.Value.UnwrapConversions()));
                    break;

                case ICompoundAssignmentOperation compoundAssignment
                    when IsLocalTarget(compoundAssignment.Target, local):
                    return new List<IOperation>();

                case IIncrementOrDecrementOperation incrementOrDecrement
                    when IsLocalTarget(incrementOrDecrement.Target, local):
                    return new List<IOperation>();
            }
        }

        assignments.Sort(static (left, right) => left.Position.CompareTo(right.Position));
        return assignments.ConvertAll(static assignment => assignment.Value);
    }

    private static bool IsLocalTarget(IOperation target, ILocalSymbol local)
    {
        return target.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, local);
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation executableRoot)
    {
        yield return executableRoot;

        foreach (var operation in executableRoot.Descendants())
        {
            if (IsInsideNestedExecutable(operation, executableRoot))
                continue;

            yield return operation;
        }
    }

    private static bool IsInsideNestedExecutable(IOperation operation, IOperation executableRoot)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ILocalFunctionOperation or IAnonymousFunctionOperation)
                return true;

            current = current.Parent;
        }

        return false;
    }
}
