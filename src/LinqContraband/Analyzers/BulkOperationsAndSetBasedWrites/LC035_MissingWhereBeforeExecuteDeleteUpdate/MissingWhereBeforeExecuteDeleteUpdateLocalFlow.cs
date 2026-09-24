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
    // One read of a local can be reached along many paths: each optional `q = q.TagWith(...)` or
    // `var q2 = q1` doubles them. Remembering each read's answer keeps the walk linear, where
    // exploring every path again took more than 15 minutes on Bitwarden.
    private sealed class LocalFlowState
    {
        public HashSet<(ILocalSymbol Local, int Position)> InProgress { get; } = new();
        public Dictionary<(ILocalSymbol Local, int Position), bool> Results { get; } = new();
    }

    private static bool HasWhereInLocalInitializer(
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken,
        LocalFlowState visitedLocals)
    {
        var key = (localReference.Local, localReference.Syntax.SpanStart);
        if (visitedLocals.Results.TryGetValue(key, out var known))
            return known;

        if (!visitedLocals.InProgress.Add(key))
            return false;

        var result = HasWhereInLocalInitializerCore(localReference, cancellationToken, visitedLocals);
        visitedLocals.InProgress.Remove(key);
        visitedLocals.Results[key] = result;
        return result;
    }

    private static bool HasWhereInLocalInitializerCore(
        ILocalReferenceOperation localReference,
        CancellationToken cancellationToken,
        LocalFlowState visitedLocals)
    {

        var executableRoot = localReference.FindOwningExecutableRoot();
        if (executableRoot == null)
            return false;

        // Split assignments into the latest unconditional base and later conditional branches.
        LocalAssignment? latestUnconditional = null;
        var conditionalReassignments = new List<LocalAssignment>();

        var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);

        foreach (var assignment in assignments)
        {
            // In `q = q.TagWith(...)` the read belongs to the assignment it sits in; it sees the
            // value `q` held before that assignment.
            if (assignment.SpanStart >= localReference.Syntax.SpanStart ||
                assignment.Value.Syntax.Span.Contains(localReference.Syntax.SpanStart))
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
                visitedLocals))
            return false;

        foreach (var conditional in conditionalReassignments)
        {
            if (conditional.SpanStart <= latestUnconditional.Value.SpanStart)
                continue;

            if (!HasWhereInChain(
                    conditional.Value.UnwrapConversions(),
                    cancellationToken,
                    visitedLocals))
                return false;
        }

        return true;
    }

    private static bool HasWhereInExhaustiveIfElseAssignments(
        IReadOnlyList<LocalAssignment> assignments,
        int beforePosition,
        CancellationToken cancellationToken,
        LocalFlowState visitedLocals)
    {
        var earlierAssignments = assignments
            .Where(assignment => assignment.SpanStart < beforePosition &&
                                 !assignment.Value.Syntax.Span.Contains(beforePosition))
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
                        visitedLocals)) &&
                elseAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        visitedLocals)) &&
                laterAssignments.All(assignment =>
                    HasWhereInChain(
                        assignment.Value.UnwrapConversions(),
                        cancellationToken,
                        visitedLocals)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsControlFlowConditionalAssignment(SyntaxNode syntax)
    {
        return syntax.Ancestors().Any(ancestor =>
            ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
                ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                TryStatementSyntax or CatchClauseSyntax);
    }
}
