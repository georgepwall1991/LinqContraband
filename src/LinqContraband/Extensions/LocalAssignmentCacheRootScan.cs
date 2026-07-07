using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

internal static partial class LocalAssignmentCache
{
    private sealed class RootScan
    {
        private static readonly IReadOnlyList<LocalAssignment> EmptyAssignments = Array.Empty<LocalAssignment>();

        private readonly Dictionary<ILocalSymbol, List<LocalAssignment>> assignmentsByLocal;

        private RootScan(Dictionary<ILocalSymbol, List<LocalAssignment>> assignmentsByLocal)
        {
            this.assignmentsByLocal = assignmentsByLocal;
        }

        public IReadOnlyList<LocalAssignment> GetAssignments(ILocalSymbol local)
        {
            return assignmentsByLocal.TryGetValue(local, out var list)
                ? list
                : EmptyAssignments;
        }

        public static RootScan Build(IOperation executableRoot, CancellationToken cancellationToken)
        {
            var assignments = new Dictionary<ILocalSymbol, List<LocalAssignment>>(SymbolEqualityComparer.Default);

            foreach (var descendant in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (descendant)
                {
                    case IVariableDeclaratorOperation declarator
                        when declarator.Initializer != null:
                        Add(assignments, declarator.Symbol, declarator.Syntax.SpanStart, declarator.Initializer.Value);
                        break;

                    case ISimpleAssignmentOperation assignment
                        when assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal:
                        Add(assignments, targetLocal.Local, assignment.Syntax.SpanStart, assignment.Value);
                        break;
                }
            }

            return new RootScan(assignments);
        }

        private static void Add(
            Dictionary<ILocalSymbol, List<LocalAssignment>> assignments,
            ILocalSymbol local,
            int spanStart,
            IOperation value)
        {
            if (!assignments.TryGetValue(local, out var list))
            {
                list = new List<LocalAssignment>();
                assignments[local] = list;
            }

            list.Add(new LocalAssignment(spanStart, value));
        }
    }
}
