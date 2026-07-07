using System.Collections.Generic;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

public sealed partial class AsNoTrackingThenModifyAnalyzer
{
    private static void TryReportForLocal(
        OperationAnalysisContext context,
        IOperation root,
        IInvocationOperation save,
        ISymbol saveContext,
        int saveSpan,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        int declSpan,
        IOperation initializer,
        HashSet<ILocalSymbol> reported)
    {
        if (reported.Contains(local)) return;
        if (declSpan >= saveSpan) return;

        if (!IsAsNoTrackingMaterialization(initializer, out var queryReceiver)) return;
        if (!TryGetQueryContextSymbol(queryReceiver, out var queryContextSymbol)) return;
        if (!SymbolEqualityComparer.Default.Equals(saveContext, queryContextSymbol)) return;

        if (HasMultipleAssignments(root, local)) return;

        var mutationNullable = FindFirstPropertyMutation(scan, local, declSpan, saveSpan);
        if (mutationNullable == null) return;
        var mutation = mutationNullable.Value;

        if (!BlockReaches(mutation.Operation, save)) return;

        if (HasEarlierSaveChangesOnSameContext(scan, saveContext, mutation.Operation.Syntax.SpanStart, saveSpan))
            return;

        if (HasDominatingPriorReattach(scan, local, saveContext, declSpan, mutation.Operation, save))
            return;

        if (HasReattach(scan, local, saveContext, mutation.Operation.Syntax.SpanStart, save))
            return;

        reported.Add(local);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            mutation.TargetLocation,
            local.Name,
            mutation.PropertyName));
    }

    private static void TryReportForForeach(
        OperationAnalysisContext context,
        IOperation root,
        IInvocationOperation save,
        ISymbol saveContext,
        int saveSpan,
        AsNoTrackingThenModifyRootScan scan,
        IForEachLoopOperation forEach,
        HashSet<ILocalSymbol> reported)
    {
        if (forEach.Syntax.SpanStart >= saveSpan) return;

        var collection = forEach.Collection.UnwrapConversions();
        if (!ChainContainsAsNoTracking(collection, out var queryReceiver)) return;
        if (!TryGetQueryContextSymbol(queryReceiver, out var queryContextSymbol)) return;
        if (!SymbolEqualityComparer.Default.Equals(saveContext, queryContextSymbol)) return;

        var forEachSpan = forEach.Syntax.Span;

        foreach (var loopLocal in forEach.Locals)
        {
            if (reported.Contains(loopLocal)) continue;

            var mutationNullable = FindFirstPropertyMutation(scan, loopLocal, forEach.Syntax.SpanStart - 1, saveSpan);
            if (mutationNullable == null) continue;
            var mutation = mutationNullable.Value;

            if (HasReattachInRange(scan, loopLocal, saveContext, forEachSpan, save)) continue;
            if (!BlockReaches(forEach, save)) continue;

            if (HasEarlierSaveChangesOnSameContext(scan, saveContext, forEach.Syntax.SpanStart, saveSpan))
                continue;

            reported.Add(loopLocal);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                mutation.TargetLocation,
                loopLocal.Name,
                mutation.PropertyName));
        }
    }

    private readonly struct MutationHit
    {
        public MutationHit(IOperation operation, Location targetLocation, string propertyName)
        {
            Operation = operation;
            TargetLocation = targetLocation;
            PropertyName = propertyName;
        }

        public IOperation Operation { get; }
        public Location TargetLocation { get; }
        public string PropertyName { get; }
    }

    private static MutationHit? FindFirstPropertyMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        int afterSpan,
        int beforeSpan)
    {
        if (!scan.MutationsByLocal.TryGetValue(local, out var mutations)) return null;

        MutationHit? best = null;
        for (var i = 0; i < mutations.Count; i++)
        {
            var entry = mutations[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan) continue;
            if (best == null || entry.SpanStart < best.Value.Operation.Syntax.SpanStart)
            {
                best = new MutationHit(entry.Operation, entry.TargetLocation, entry.PropertyName);
            }
        }

        return best;
    }

    private static bool HasMultipleAssignments(IOperation root, ILocalSymbol local)
    {
        var assignments = LocalAssignmentCache.GetAssignments(root, local);
        return assignments.Count > 1;
    }
}
