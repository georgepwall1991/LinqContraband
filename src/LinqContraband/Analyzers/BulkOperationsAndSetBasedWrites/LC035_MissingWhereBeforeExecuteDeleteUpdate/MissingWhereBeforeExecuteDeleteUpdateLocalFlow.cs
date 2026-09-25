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
        public LocalFlowState(bool trackParameters, HashSet<IMethodSymbol>? helpersInProgress = null)
        {
            ParameterRoots = trackParameters ? new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default) : null;
            HelpersInProgress = helpersInProgress ?? new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        }

        public HashSet<(ILocalSymbol Local, int Position)> InProgress { get; } = new();
        public Dictionary<(ILocalSymbol Local, int Position), bool> Results { get; } = new();

        // When set, a query that starts at a parameter whose callers can be found counts as filtered
        // for now; the parameter is recorded and its call sites decide at compilation end.
        public HashSet<IParameterSymbol>? ParameterRoots { get; }

        // Filter helpers whose bodies are being read, so a helper that calls itself ends the walk.
        public HashSet<IMethodSymbol> HelpersInProgress { get; }
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
        var read = localReference.Syntax;
        var laterAssignmentsInEnclosingLoops = new List<LocalAssignment>();

        foreach (var assignment in assignments)
        {
            // In `q = q.TagWith(...)` the read belongs to the assignment it sits in; it sees the
            // value `q` held before that assignment.
            if (assignment.Value.Syntax.Span.Contains(read.SpanStart))
                continue;

            if (assignment.SpanStart >= read.SpanStart)
            {
                // A later assignment inside a loop that also holds the read reaches it on the next pass.
                if (IsInLoopEnclosingRead(assignment.Value.Syntax, read, executableRoot.Syntax))
                    laterAssignmentsInEnclosingLoops.Add(assignment);
                continue;
            }

            if (IsControlFlowConditionalAssignment(assignment.Value.Syntax, read))
                conditionalReassignments.Add(assignment);
            else if (latestUnconditional == null || assignment.SpanStart > latestUnconditional.Value.SpanStart)
                latestUnconditional = assignment;
        }

        foreach (var later in laterAssignmentsInEnclosingLoops)
        {
            // An unconditional assignment in the same loop, before the read, overwrites it first.
            if (latestUnconditional != null &&
                SharesEnclosingLoop(later.Value.Syntax, latestUnconditional.Value.Value.Syntax, read))
                continue;

            if (latestUnconditional == null &&
                assignments.Any(assignment => assignment.SpanStart < read.SpanStart &&
                                              SharesEnclosingLoop(later.Value.Syntax, assignment.Value.Syntax, read)))
                continue;

            if (!HasWhereInChain(later.Value.UnwrapConversions(), cancellationToken, visitedLocals))
                return false;
        }

        if (latestUnconditional == null)
            return HasWhereInExhaustiveIfElseAssignments(
                assignments,
                read.SpanStart,
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

    // An assignment is conditional for a read when a branch, loop or try separates them. A construct
    // that holds both (the read in the same loop body or try block) runs the assignment first on every
    // path, so it does not make the assignment optional. When the construct is where the two meet, such
    // as an assignment in a try block read from its catch, the assignment stays optional.
    private static bool IsControlFlowConditionalAssignment(SyntaxNode syntax, SyntaxNode read)
    {
        foreach (var ancestor in syntax.Ancestors())
        {
            if (IsConditionalConstruct(ancestor))
                return true;

            if (ancestor.Span.Contains(read.Span))
                return false;
        }

        return false;
    }

    private static bool IsConditionalConstruct(SyntaxNode node)
    {
        return node is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
            ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
            TryStatementSyntax or CatchClauseSyntax;
    }

    private static bool IsLoop(SyntaxNode node)
    {
        return node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;
    }

    private static bool IsInLoopEnclosingRead(SyntaxNode assignment, SyntaxNode read, SyntaxNode rootSyntax)
    {
        return assignment.Ancestors()
            .TakeWhile(ancestor => ancestor != rootSyntax)
            .Any(ancestor => IsLoop(ancestor) && ancestor.Span.Contains(read.Span));
    }

    // The later assignment reaches the read through the back edge of the innermost loop holding both.
    // An earlier assignment inside that loop runs again on every pass before the read, so it wins.
    private static bool SharesEnclosingLoop(SyntaxNode laterAssignment, SyntaxNode earlierAssignment, SyntaxNode read)
    {
        var innermostLoop = laterAssignment.Ancestors()
            .FirstOrDefault(ancestor => IsLoop(ancestor) && ancestor.Span.Contains(read.Span));
        return innermostLoop != null && innermostLoop.Span.Contains(earlierAssignment.Span);
    }
}
