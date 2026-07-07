using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC035_MissingWhereBeforeExecuteDeleteUpdate;

public sealed partial class MissingWhereBeforeExecuteDeleteUpdateAnalyzer
{
    private static bool HasWhereInLocalInitializer(
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        if (!visitedLocals.Add(localReference.Local))
            return false;

        var executableRoot = localReference.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        // Split assignments into the latest unconditional base and later conditional branches.
        LocalAssignment? latestUnconditional = null;
        var conditionalReassignments = new List<LocalAssignment>();

        var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);

        foreach (var assignment in assignments)
        {
            if (assignment.SpanStart >= localReference.Syntax.SpanStart)
                continue;

            if (IsControlFlowConditionalAssignment(assignment.Value.Syntax))
                conditionalReassignments.Add(assignment);
            else if (latestUnconditional == null || assignment.SpanStart > latestUnconditional.Value.SpanStart)
                latestUnconditional = assignment;
        }

        if (latestUnconditional == null)
            return HasWhereInExhaustiveIfElseAssignments(
                assignments,
                localReference.Syntax.SpanStart,
                cancellationToken,
                visitedLocals);

        if (!HasWhereInChain(
                latestUnconditional.Value.Value.UnwrapConversions(),
                cancellationToken,
                ForkVisitedLocals(visitedLocals)))
            return false;

        foreach (var conditional in conditionalReassignments)
        {
            if (conditional.SpanStart <= latestUnconditional.Value.SpanStart)
                continue;

            if (!HasWhereInChain(
                    conditional.Value.UnwrapConversions(),
                    cancellationToken,
                    ForkVisitedLocals(visitedLocals)))
                return false;
        }

        return true;
    }

    private static bool HasWhereInExhaustiveIfElseAssignments(
        IReadOnlyList<LocalAssignment> assignments,
        int beforePosition,
        CancellationToken cancellationToken,
        ISet<ILocalSymbol> visitedLocals)
    {
        var earlierAssignments = assignments
            .Where(assignment => assignment.SpanStart < beforePosition)
            .ToArray();
        if (earlierAssignments.Length == 0)
            return false;

        foreach (var ifStatement in earlierAssignments
                     .Select(assignment => assignment.Value.Syntax.FirstAncestorOrSelf<IfStatementSyntax>())
                     .Where(ifStatement => ifStatement?.Else != null)
                     .Distinct())
        {
            if (ifStatement == null)
                continue;

            var assignmentsFromCandidate = earlierAssignments
                .Where(assignment => assignment.SpanStart >= ifStatement.SpanStart)
                .ToArray();

            var thenAssignments = earlierAssignments
                .Where(assignment => ifStatement.Statement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();
            var elseAssignments = earlierAssignments
                .Where(assignment => ifStatement.Else!.Statement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();
            var laterAssignments = assignmentsFromCandidate
                .Where(assignment => !ifStatement.Span.Contains(assignment.Value.Syntax.Span))
                .ToArray();

            if (thenAssignments.Length == 0 || elseAssignments.Length == 0)
                continue;

            if (thenAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))) &&
                elseAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))) &&
                laterAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        ForkVisitedLocals(visitedLocals))))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<ILocalSymbol> ForkVisitedLocals(ISet<ILocalSymbol> visitedLocals)
    {
        return new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default);
    }

    private static bool IsControlFlowConditionalAssignment(SyntaxNode syntax)
    {
        return syntax.Ancestors().Any(ancestor =>
            ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
                ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                TryStatementSyntax or CatchClauseSyntax);
    }
}
