using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static bool HasTerminatorBetween(
        System.Collections.Immutable.ImmutableArray<IOperation> operations,
        int afterSpanStart,
        int beforeSpanStart,
        SyntaxNode saveSyntax)
    {
        foreach (var operation in operations)
        {
            if (operation.Syntax.SpanStart <= afterSpanStart || operation.Syntax.SpanStart >= beforeSpanStart)
                continue;

            // Unconditional exits that leave the current executable root block reachability.
            if (operation is IReturnOperation or IThrowOperation)
                return true;

            // Branch operations that skip the remainder of the current region can break
            // reachability. `break` exits a loop/switch; `continue` jumps to the loop
            // condition. Both only block the save when the save is lexically inside the
            // same construct and after the branch. `goto` is treated conservatively as a
            // terminator because its target is harder to resolve.
            if (operation is IBranchOperation branch)
            {
                if (branch.BranchKind == BranchKind.GoTo)
                    return true;

                if (branch.BranchKind == BranchKind.Break && IsBreakBlocking(branch, saveSyntax))
                    return true;

                if (branch.BranchKind == BranchKind.Continue && IsContinueBlocking(branch, saveSyntax))
                    return true;
            }
        }

        return false;
    }

    private static bool IsBreakBlocking(IBranchOperation branch, SyntaxNode saveSyntax)
    {
        var breakSyntax = branch.Syntax;
        var enclosingConstruct = breakSyntax.Ancestors().FirstOrDefault(a =>
            a.IsKind(SyntaxKind.WhileStatement) ||
            a.IsKind(SyntaxKind.DoStatement) ||
            a.IsKind(SyntaxKind.ForStatement) ||
            a.IsKind(SyntaxKind.ForEachStatement) ||
            a.IsKind(SyntaxKind.ForEachVariableStatement) ||
            a.IsKind(SyntaxKind.SwitchStatement));

        if (enclosingConstruct == null)
            return true;

        // If the SaveChanges is after the loop/switch, the break transfers control to it,
        // so the mutation can still reach the save.
        if (!saveSyntax.AncestorsAndSelf().Contains(enclosingConstruct))
            return false;

        // If the SaveChanges is inside the same construct but before the break, it is on a
        // different path; the break only matters when the save is lexically after it.
        return saveSyntax.SpanStart > breakSyntax.SpanStart;
    }

    private static bool IsContinueBlocking(IBranchOperation branch, SyntaxNode saveSyntax)
    {
        var continueSyntax = branch.Syntax;
        var enclosingLoop = continueSyntax.Ancestors().FirstOrDefault(a =>
            a.IsKind(SyntaxKind.WhileStatement) ||
            a.IsKind(SyntaxKind.DoStatement) ||
            a.IsKind(SyntaxKind.ForStatement) ||
            a.IsKind(SyntaxKind.ForEachStatement) ||
            a.IsKind(SyntaxKind.ForEachVariableStatement));

        if (enclosingLoop == null)
            return true;

        // A continue jumps to the loop condition. If the SaveChanges is after the loop,
        // the mutation can still reach it when the loop exits, so the continue does not block.
        if (!saveSyntax.AncestorsAndSelf().Contains(enclosingLoop))
            return false;

        // If the SaveChanges is inside the same loop but before the continue, it is on a
        // different path; the continue only matters when the save is lexically after it.
        return saveSyntax.SpanStart > continueSyntax.SpanStart;
    }
}
