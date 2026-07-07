using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateAnalyzer
{
    private bool IsFromNoTrackingQuery(ILocalSymbol local, IOperation currentOperation)
    {
        return IsFromNoTrackingQuery(local, currentOperation, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private bool IsFromNoTrackingQuery(ILocalSymbol local, IOperation currentOperation, ISet<ISymbol> visited)
    {
        if (!visited.Add(local)) return false;

        var root = currentOperation.FindOwningExecutableRoot() ?? currentOperation;
        var currentStart = currentOperation.Syntax.SpanStart;
        var origins = new List<LocalOrigin>();

        foreach (var op in EnumerateOperations(root))
        {
            // 1. Standard Assignments
            if (op is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                assignment.Syntax.SpanStart < currentStart)
            {
                origins.Add(new LocalOrigin(
                    assignment.Syntax.SpanStart,
                    IsNoTrackingSource(assignment.Value, assignment, visited),
                    assignment.Syntax));
            }

            // 2. Variable Declarations
            if (op is IVariableDeclarationOperation decl)
            {
                foreach (var declarator in decl.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                        declarator.Initializer != null &&
                        declarator.Syntax.SpanStart < currentStart)
                    {
                        origins.Add(new LocalOrigin(
                            declarator.Syntax.SpanStart,
                            IsNoTrackingSource(declarator.Initializer.Value, declarator.Initializer.Value, visited),
                            declarator.Syntax));
                    }
                }
            }

            // 3. Foreach Loops
            if (op is IForEachLoopOperation forEach)
            {
                // Check if our target 'local' is one of the locals defined by the loop
                if (forEach.Locals.Any(l => SymbolEqualityComparer.Default.Equals(l, local)) &&
                    forEach.Syntax.Span.Contains(currentStart) &&
                    forEach.Collection.Syntax.SpanStart < currentStart)
                {
                    // The loop local is rebound from the collection on every iteration the
                    // use participates in, so this origin is never conditional.
                    origins.Add(new LocalOrigin(
                        forEach.Collection.Syntax.SpanStart,
                        IsNoTrackingSource(forEach.Collection, forEach.Collection, visited),
                        null));
                }
            }
        }

        if (origins.Count == 0) return false;

        var best = origins[0];
        for (var i = 1; i < origins.Count; i++)
        {
            if (origins[i].Position >= best.Position)
                best = origins[i];
        }

        if (!best.IsNoTracking) return false;

        // Path-insensitivity guard: when the latest origin sits in a branch that may not
        // execute before the use, the effective state on the skip path is whatever the
        // latest UNCONDITIONAL origin before it established (anything earlier is dead,
        // superseded history). If that fallback disagrees — or only conditional origins
        // exist at all — the verdict depends on the path taken: stay quiet (same ambiguity
        // trade-off as LC044's multiple-assignment gate). An unconditional latest origin
        // dominates and needs no guard.
        if (IsConditionalRelativeTo(best.Syntax, currentStart, root.Syntax))
        {
            LocalOrigin? fallback = null;
            foreach (var origin in origins)
            {
                if (origin.Position < best.Position &&
                    (fallback == null || origin.Position > fallback.Value.Position) &&
                    !IsConditionalRelativeTo(origin.Syntax, currentStart, root.Syntax))
                {
                    fallback = origin;
                }
            }

            if (fallback == null || !fallback.Value.IsNoTracking)
                return false;
        }

        return true;
    }

}
