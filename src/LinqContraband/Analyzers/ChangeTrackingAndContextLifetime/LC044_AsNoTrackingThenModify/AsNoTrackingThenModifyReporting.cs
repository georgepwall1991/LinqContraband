using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

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

        var mutationNullable = FindFirstPropertyMutation(
            scan, local, saveContext, declSpan, saveSpan, save);
        if (mutationNullable == null) return;
        var mutation = mutationNullable.Value;

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
        if (!BlockReaches(forEach, save)) return;

        foreach (var loopLocal in forEach.Locals)
        {
            if (reported.Contains(loopLocal)) continue;

            var mutationNullable = FindFirstUnpersistedForeachMutation(
                scan,
                loopLocal,
                saveContext,
                forEach.Syntax.SpanStart - 1,
                saveSpan,
                forEachSpan,
                save);
            if (mutationNullable == null) continue;
            var mutation = mutationNullable.Value;

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
        public MutationHit(
            IOperation operation,
            Location targetLocation,
            string propertyName,
            ImmutableArray<MemberPathSegment> receiverPath)
        {
            Operation = operation;
            TargetLocation = targetLocation;
            PropertyName = propertyName;
            ReceiverPath = receiverPath;
        }

        public IOperation Operation { get; }
        public Location TargetLocation { get; }
        public string PropertyName { get; }
        public ImmutableArray<MemberPathSegment> ReceiverPath { get; }
    }

    private static MutationHit? FindFirstPropertyMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        int beforeSpan,
        IOperation save)
    {
        if (!scan.MutationsByLocal.TryGetValue(local, out var mutations)) return null;

        MutationHit? best = null;
        for (var i = 0; i < mutations.Count; i++)
        {
            var entry = mutations[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan) continue;
            if (!BlockReaches(entry.Operation, save)) continue;
            if (HasEarlierSaveChangesOnSameContext(scan, saveContext, entry.Operation, save))
                continue;
            if (HasDominatingPriorReattach(
                    scan,
                    local,
                    saveContext,
                    entry.ReceiverPath,
                    afterSpan,
                    entry.Operation,
                    save))
            {
                continue;
            }
            if (HasReattach(
                    scan,
                    local,
                    saveContext,
                    entry.ReceiverPath,
                    entry.Operation,
                    save))
            {
                continue;
            }

            best = EarlierMutation(best, entry);
        }

        return best;
    }

    private static MutationHit? FindFirstUnpersistedForeachMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        int beforeSpan,
        TextSpan forEachSpan,
        IOperation save)
    {
        if (!scan.MutationsByLocal.TryGetValue(local, out var mutations)) return null;

        MutationHit? best = null;
        for (var i = 0; i < mutations.Count; i++)
        {
            var entry = mutations[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan) continue;
            if (!BlockReaches(entry.Operation, save)) continue;
            if (HasEarlierSaveChangesOnSameContext(scan, saveContext, entry.Operation, save))
                continue;
            if (HasReattachInRange(
                    scan, local, saveContext, entry.ReceiverPath, entry.Operation, forEachSpan, save))
            {
                continue;
            }

            best = EarlierMutation(best, entry);
        }

        return best;
    }

    private static MutationHit EarlierMutation(MutationHit? current, MutationEntry candidate)
    {
        if (current == null || candidate.SpanStart < current.Value.Operation.Syntax.SpanStart)
        {
            return new MutationHit(
                candidate.Operation,
                candidate.TargetLocation,
                candidate.PropertyName,
                candidate.ReceiverPath);
        }

        return current.Value;
    }

    private static bool HasMultipleAssignments(IOperation root, ILocalSymbol local)
    {
        var assignments = LocalAssignmentCache.GetAssignments(root, local);
        return assignments.Count > 1;
    }
}
