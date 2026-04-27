using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC044_AsNoTrackingThenModify;

/// <summary>
/// Detects entities loaded via AsNoTracking() that are mutated and then passed through SaveChanges
/// on the same context without any re-attach (Update / Attach / Entry.State = Modified). EF silently
/// persists nothing in this case. Diagnostic ID: LC044.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsNoTrackingThenModifyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC044";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title =
        "AsNoTracking query mutated then SaveChanges — silent data loss";

    private static readonly LocalizableString MessageFormat =
        "Entity '{0}' was loaded with AsNoTracking() and property '{1}' is mutated before SaveChanges — the change will not persist. Remove AsNoTracking(), or call Update/Attach or set Entry(entity).State = Modified before SaveChanges.";

    private static readonly LocalizableString Description =
        "EF Core does not track entities materialized from an AsNoTracking() query. Mutating a property of such an entity and then calling SaveChanges silently results in no database write. This rule flags the chain AsNoTracking-origin \u2192 property mutation \u2192 SaveChanges on the same context when no re-attach (Update / Attach / Entry.State = Modified / Added) intervenes.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC044_AsNoTrackingThenModifySilentWrite.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeSaveChanges, OperationKind.Invocation);
    }

    private static void AnalyzeSaveChanges(OperationAnalysisContext context)
    {
        var save = (IInvocationOperation)context.Operation;
        var method = save.TargetMethod;

        if (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync") return;
        if (!method.ContainingType.IsDbContext()) return;

        if (!AsNoTrackingThenModifyRootScan.TryGetSymbol(save.Instance, out var saveContextSymbol) ||
            saveContextSymbol == null) return;

        var root = save.FindOwningExecutableRoot();
        if (root == null) return;

        var scan = AsNoTrackingThenModifyRootScan.GetOrBuild(root, context.CancellationToken);
        var saveSpan = save.Syntax.SpanStart;
        var reported = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var decl in scan.InitializedDeclarators)
        {
            TryReportForLocal(
                context, root, save, saveContextSymbol, saveSpan, scan,
                decl.Symbol, decl.Syntax.SpanStart, decl.Initializer!.Value, reported);
        }

        foreach (var forEach in scan.ForEachLoops)
        {
            TryReportForForeach(context, root, save, saveContextSymbol, saveSpan, scan, forEach, reported);
        }
    }

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

        if (HasReattach(scan, local, saveContext, mutation.Operation.Syntax.SpanStart, saveSpan))
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

            if (HasReattachInRange(scan, loopLocal, saveContext, forEachSpan)) continue;
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

    private static bool HasReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        int beforeSpan)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReattachInRange(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        TextSpan range)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (!range.Contains(entry.Span)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEarlierSaveChangesOnSameContext(
        AsNoTrackingThenModifyRootScan scan,
        ISymbol saveContext,
        int afterSpan,
        int currentSaveSpan)
    {
        var saves = scan.SaveChangesCalls;
        for (var i = 0; i < saves.Count; i++)
        {
            var entry = saves[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= currentSaveSpan) continue;
            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BlockReaches(IOperation mutation, IOperation saveChanges)
    {
        var mutationBlock = FindEnclosingBlock(mutation);
        if (mutationBlock == null) return true;

        var current = saveChanges.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, mutationBlock)) return true;
            current = current.Parent;
        }

        return false;
    }

    private static IBlockOperation? FindEnclosingBlock(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IBlockOperation block) return block;
            current = current.Parent;
        }

        return null;
    }

    private static bool IsAsNoTrackingMaterialization(IOperation operation, out IOperation? queryReceiver)
    {
        queryReceiver = null;
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is not IInvocationOperation invocation) return false;

        if (!invocation.TargetMethod.Name.IsMaterializerMethod()) return false;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null) return false;

        if (!ChainContainsAsNoTracking(receiver, out queryReceiver)) return false;

        return true;
    }

    private static bool ChainContainsAsNoTracking(IOperation operation, out IOperation? rootReceiver)
    {
        rootReceiver = null;
        var current = operation.UnwrapConversions();
        var sawAsNoTracking = false;

        while (current is IInvocationOperation inv)
        {
            if (inv.TargetMethod.Name == "AsNoTracking")
                sawAsNoTracking = true;

            var next = inv.GetInvocationReceiver();
            if (next == null)
            {
                rootReceiver = inv;
                break;
            }

            current = next.UnwrapConversions();
        }

        if (!sawAsNoTracking) return false;

        if (rootReceiver == null) rootReceiver = current;
        return true;
    }

    private static bool TryGetQueryContextSymbol(IOperation? queryReceiver, out ISymbol? contextSymbol)
    {
        contextSymbol = null;
        if (queryReceiver == null) return false;

        var current = queryReceiver.UnwrapConversions();

        while (current != null)
        {
            switch (current)
            {
                case IPropertyReferenceOperation propRef when propRef.Type.IsDbSet():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(propRef.Instance, out contextSymbol);

                case IFieldReferenceOperation fieldRef when fieldRef.Type.IsDbSet():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(fieldRef.Instance, out contextSymbol);

                case IInvocationOperation inv when inv.TargetMethod.Name == "Set" &&
                                                   inv.TargetMethod.ContainingType.IsDbContext():
                    return AsNoTrackingThenModifyRootScan.TryGetSymbol(inv.Instance, out contextSymbol);

                case IInvocationOperation inv:
                    var next = inv.GetInvocationReceiver();
                    if (next == null) return false;
                    current = next.UnwrapConversions();
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }
}
