using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private static bool TryGetSetters(ForEachStatementSyntax forEach, string iterationName, out ImmutableArray<(string Left, string Right)> setters)
    {
        setters = ImmutableArray<(string, string)>.Empty;

        var statements = forEach.Statement is BlockSyntax block
            ? (IReadOnlyList<StatementSyntax>)block.Statements
            : new[] { forEach.Statement };

        if (statements.Count == 0)
            return false;

        var assignments = new List<(string Left, string PropertyName, AssignmentExpressionSyntax Node)>();

        foreach (var statement in statements)
        {
            if (statement is not ExpressionStatementSyntax expressionStatement)
                return false;

            if (expressionStatement.Expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.Left is not MemberAccessExpressionSyntax target)
            {
                return false;
            }

            assignments.Add((assignment.Left.WithoutTrivia().ToString(), target.Name.Identifier.Text, assignment));
        }

        // ExecuteUpdate evaluates every value expression against the ORIGINAL row, so the
        // set-based rewrite only matches the loop's sequential semantics when no value reads a
        // property written earlier in the same iteration (e.g. a second `user.Name = user.Name
        // + "!"` would read the pre-loop value, not the just-assigned one). Decline that shape.
        var writtenSoFar = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, propertyName, node) in assignments)
        {
            if (ReadsAnyProperty(node.Right, iterationName, writtenSoFar))
                return false;

            writtenSoFar.Add(propertyName);
        }

        // Collapse duplicate targets to the last write (their values are independent of earlier
        // writes by the check above), preserving first-seen order.
        var order = new List<string>();
        var lastValue = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (left, _, node) in assignments)
        {
            if (!lastValue.ContainsKey(left))
                order.Add(left);

            lastValue[left] = node.Right.WithoutTrivia().ToString();
        }

        setters = order.Select(left => (left, lastValue[left])).ToImmutableArray();
        return setters.Length > 0;
    }

    private static bool ReadsAnyProperty(ExpressionSyntax expression, string iterationName, HashSet<string> propertyNames)
    {
        // Matching `iterationVar.Prop` is sufficient here: the analyzer has already restricted every
        // RHS to direct scalar members of the iteration variable (see the assignment analysis), so
        // deeper shapes like `iterationVar.Nav.Prop` never reach the fixer.
        if (propertyNames.Count == 0)
            return false;

        foreach (var member in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (member.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == iterationName &&
                propertyNames.Contains(member.Name.Identifier.Text))
            {
                return true;
            }
        }

        return false;
    }
}
