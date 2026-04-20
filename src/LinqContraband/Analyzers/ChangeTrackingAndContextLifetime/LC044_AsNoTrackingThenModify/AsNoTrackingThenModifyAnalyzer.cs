using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC044_AsNoTrackingThenModifySilentWrite.md");

    private static readonly ImmutableHashSet<string> ReattachMethodNames = ImmutableHashSet.Create(
        "Update", "UpdateRange", "Attach", "AttachRange");

    private static readonly ImmutableHashSet<string> TrackingStates = ImmutableHashSet.Create(
        "Modified", "Added");

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

        if (!TryGetSymbol(save.Instance, out var saveContextSymbol) || saveContextSymbol == null) return;

        var root = save.FindOwningExecutableRoot();
        if (root == null) return;

        var saveSpan = save.Syntax.SpanStart;
        var reported = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var op in Descendants(root))
        {
            switch (op)
            {
                case IVariableDeclaratorOperation decl when decl.Initializer?.Value != null:
                    TryReportForLocal(
                        context, root, save, saveContextSymbol, saveSpan,
                        decl.Symbol, decl.Syntax.SpanStart, decl.Initializer.Value, reported);
                    break;

                case IForEachLoopOperation forEach:
                    TryReportForForeach(context, root, save, saveContextSymbol, saveSpan, forEach, reported);
                    break;
            }
        }
    }

    private static void TryReportForLocal(
        OperationAnalysisContext context,
        IOperation root,
        IInvocationOperation save,
        ISymbol saveContext,
        int saveSpan,
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

        var mutationNullable = FindFirstPropertyMutation(root, local, declSpan, saveSpan);
        if (mutationNullable == null) return;
        var mutation = mutationNullable.Value;

        if (!BlockReaches(mutation.Operation, save)) return;

        if (HasEarlierSaveChangesOnSameContext(root, saveContext, mutation.Operation.Syntax.SpanStart, saveSpan))
            return;

        if (HasReattach(root, local, saveContext, mutation.Operation.Syntax.SpanStart, saveSpan))
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
        IForEachLoopOperation forEach,
        HashSet<ILocalSymbol> reported)
    {
        if (forEach.Syntax.SpanStart >= saveSpan) return;

        var collection = forEach.Collection.UnwrapConversions();
        if (!ChainContainsAsNoTracking(collection, out var queryReceiver)) return;
        if (!TryGetQueryContextSymbol(queryReceiver, out var queryContextSymbol)) return;
        if (!SymbolEqualityComparer.Default.Equals(saveContext, queryContextSymbol)) return;

        foreach (var loopLocal in forEach.Locals)
        {
            if (reported.Contains(loopLocal)) continue;

            var mutationNullable = FindFirstPropertyMutation(forEach, loopLocal, forEach.Syntax.SpanStart - 1, saveSpan);
            if (mutationNullable == null) continue;
            var mutation = mutationNullable.Value;

            if (HasReattachInRange(forEach, loopLocal, saveContext)) continue;
            if (!BlockReaches(forEach, save)) continue;

            if (HasEarlierSaveChangesOnSameContext(root, saveContext, forEach.Syntax.SpanStart, saveSpan))
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

    private static MutationHit? FindFirstPropertyMutation(IOperation root, ILocalSymbol local, int afterSpan, int beforeSpan)
    {
        MutationHit? best = null;
        foreach (var op in Descendants(root))
        {
            if (op is not ISimpleAssignmentOperation assignment) continue;
            if (assignment.Target is not IPropertyReferenceOperation propRef) continue;

            var instance = propRef.Instance?.UnwrapConversions();
            if (instance is not ILocalReferenceOperation localRef) continue;
            if (!SymbolEqualityComparer.Default.Equals(localRef.Local, local)) continue;

            var span = assignment.Syntax.SpanStart;
            if (span <= afterSpan || span >= beforeSpan) continue;

            if (best == null || span < best.Value.Operation.Syntax.SpanStart)
            {
                best = new MutationHit(assignment, propRef.Syntax.GetLocation(), propRef.Property.Name);
            }
        }

        return best;
    }

    private static bool HasMultipleAssignments(IOperation root, ILocalSymbol local)
    {
        var count = 0;
        foreach (var op in Descendants(root))
        {
            if (op is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                count++;
            }
            else if (op is IVariableDeclaratorOperation declarator &&
                     SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                     declarator.Initializer != null)
            {
                count++;
            }

            if (count > 1) return true;
        }

        return false;
    }

    private static bool HasReattach(
        IOperation root,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        int beforeSpan)
    {
        foreach (var op in Descendants(root))
        {
            var span = op.Syntax.SpanStart;
            if (span <= afterSpan || span >= beforeSpan) continue;

            if (op is IInvocationOperation inv && IsReattachInvocation(inv, local, saveContext))
                return true;

            if (op is ISimpleAssignmentOperation assign && IsEntryStateReattach(assign, local, saveContext))
                return true;
        }

        return false;
    }

    private static bool HasReattachInRange(IOperation range, ILocalSymbol local, ISymbol saveContext)
    {
        foreach (var op in Descendants(range))
        {
            if (op is IInvocationOperation inv && IsReattachInvocation(inv, local, saveContext))
                return true;

            if (op is ISimpleAssignmentOperation assign && IsEntryStateReattach(assign, local, saveContext))
                return true;
        }

        return false;
    }

    private static bool IsReattachInvocation(IInvocationOperation invocation, ILocalSymbol local, ISymbol saveContext)
    {
        if (!ReattachMethodNames.Contains(invocation.TargetMethod.Name)) return false;

        var container = invocation.TargetMethod.ContainingType;
        if (!container.IsDbContext() && !container.IsDbSet()) return false;

        if (invocation.Arguments.Length == 0) return false;
        var argValue = invocation.Arguments[0].Value.UnwrapConversions();
        if (argValue is not ILocalReferenceOperation argLocal) return false;
        if (!SymbolEqualityComparer.Default.Equals(argLocal.Local, local)) return false;

        return TryResolveInvocationContext(invocation, out var invContext)
               && SymbolEqualityComparer.Default.Equals(invContext, saveContext);
    }

    private static bool IsEntryStateReattach(ISimpleAssignmentOperation assignment, ILocalSymbol local, ISymbol saveContext)
    {
        if (assignment.Target is not IPropertyReferenceOperation targetProp) return false;
        if (targetProp.Property.Name != "State") return false;
        if (targetProp.Property.ContainingType?.Name != "EntityEntry") return false;

        if (targetProp.Instance is not IInvocationOperation entryInv) return false;
        if (entryInv.TargetMethod.Name != "Entry") return false;
        if (!entryInv.TargetMethod.ContainingType.IsDbContext()) return false;

        if (entryInv.Arguments.Length == 0) return false;
        var argValue = entryInv.Arguments[0].Value.UnwrapConversions();
        if (argValue is not ILocalReferenceOperation argLocal) return false;
        if (!SymbolEqualityComparer.Default.Equals(argLocal.Local, local)) return false;

        if (!TryGetSymbol(entryInv.Instance, out var entryContext)) return false;
        if (!SymbolEqualityComparer.Default.Equals(entryContext, saveContext)) return false;

        var value = assignment.Value.UnwrapConversions();
        if (value is not IFieldReferenceOperation fieldRef) return false;
        if (fieldRef.Field.ContainingType?.Name != "EntityState") return false;

        return TrackingStates.Contains(fieldRef.Field.Name);
    }

    private static bool TryResolveInvocationContext(IInvocationOperation invocation, out ISymbol? contextSymbol)
    {
        contextSymbol = null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
        {
            return TryGetSymbol(invocation.Instance, out contextSymbol);
        }

        if (invocation.TargetMethod.ContainingType.IsDbSet())
        {
            var dbSetInstance = invocation.Instance?.UnwrapConversions();
            switch (dbSetInstance)
            {
                case IPropertyReferenceOperation propRef when propRef.Type.IsDbSet():
                    return TryGetSymbol(propRef.Instance, out contextSymbol);
                case IFieldReferenceOperation fieldRef when fieldRef.Type.IsDbSet():
                    return TryGetSymbol(fieldRef.Instance, out contextSymbol);
                case IInvocationOperation setCall when setCall.TargetMethod.Name == "Set" &&
                                                      setCall.TargetMethod.ContainingType.IsDbContext():
                    return TryGetSymbol(setCall.Instance, out contextSymbol);
            }
        }

        return false;
    }

    private static bool HasEarlierSaveChangesOnSameContext(
        IOperation root,
        ISymbol saveContext,
        int afterSpan,
        int currentSaveSpan)
    {
        foreach (var op in Descendants(root))
        {
            if (op is not IInvocationOperation inv) continue;
            if (inv.TargetMethod.Name != "SaveChanges" && inv.TargetMethod.Name != "SaveChangesAsync") continue;
            if (!inv.TargetMethod.ContainingType.IsDbContext()) continue;

            var span = inv.Syntax.SpanStart;
            if (span <= afterSpan || span >= currentSaveSpan) continue;

            if (TryGetSymbol(inv.Instance, out var sym) &&
                SymbolEqualityComparer.Default.Equals(sym, saveContext))
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
                    return TryGetSymbol(propRef.Instance, out contextSymbol);

                case IFieldReferenceOperation fieldRef when fieldRef.Type.IsDbSet():
                    return TryGetSymbol(fieldRef.Instance, out contextSymbol);

                case IInvocationOperation inv when inv.TargetMethod.Name == "Set" &&
                                                   inv.TargetMethod.ContainingType.IsDbContext():
                    return TryGetSymbol(inv.Instance, out contextSymbol);

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

    private static bool TryGetSymbol(IOperation? operation, out ISymbol? symbol)
    {
        symbol = null;
        if (operation == null) return false;

        switch (operation.UnwrapConversions())
        {
            case ILocalReferenceOperation localRef:
                symbol = localRef.Local;
                return true;
            case IParameterReferenceOperation paramRef:
                symbol = paramRef.Parameter;
                return true;
            case IFieldReferenceOperation fieldRef:
                symbol = fieldRef.Field;
                return true;
            case IPropertyReferenceOperation propRef:
                symbol = propRef.Property;
                return true;
            case IInstanceReferenceOperation:
                symbol = null;
                return false;
            default:
                return false;
        }
    }

    private static IEnumerable<IOperation> Descendants(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
            foreach (var d in Descendants(child))
                yield return d;
    }
}
