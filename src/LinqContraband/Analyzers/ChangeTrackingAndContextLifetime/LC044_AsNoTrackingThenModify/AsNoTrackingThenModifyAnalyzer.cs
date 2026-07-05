using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    private static bool HasReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        var saveSpan = save.Syntax.SpanStart;
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= saveSpan) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(scan, local, saveContext, entry.SpanStart, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDominatingPriorReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        IOperation mutation,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        var mutationSpan = mutation.Syntax.SpanStart;
        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= mutationSpan) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                Dominates(entry.Operation, mutation) &&
                !HasInterveningDetach(scan, local, saveContext, entry.SpanStart, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInterveningDetach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        int afterSpan,
        IOperation save)
    {
        var saveSpan = save.Syntax.SpanStart;
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            for (var i = 0; i < detaches.Count; i++)
            {
                var entry = detaches[i];
                if (entry.SpanStart <= afterSpan || entry.SpanStart >= saveSpan) continue;

                if (entry.ContextSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                    BlockReaches(entry.Operation, save))
                {
                    return true;
                }
            }
        }

        var clears = scan.TrackerClears;
        for (var i = 0; i < clears.Count; i++)
        {
            var entry = clears[i];
            if (entry.SpanStart <= afterSpan || entry.SpanStart >= saveSpan) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                BlockReaches(entry.Operation, save))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Dominates(IOperation earlier, IOperation later)
    {
        if (!earlier.SharesOwningExecutableRoot(later)) return false;
        if (earlier.Syntax.SpanStart >= later.Syntax.SpanStart) return false;

        var earlierBlock = FindEnclosingBlock(earlier);
        var laterBlock = FindEnclosingBlock(later);
        if (earlierBlock == null || laterBlock == null) return true;

        if (ReferenceEquals(earlierBlock, laterBlock))
        {
            if (IsNestedUnderOptionalControlFlow(earlier, earlierBlock, later))
                return false;

            return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, later.Syntax.SpanStart, later.Syntax);
        }

        if (IsBlockAncestor(laterBlock, earlierBlock))
        {
            if (IsNestedUnderOptionalControlFlow(earlier, laterBlock, later))
                return false;

            return BlockReaches(earlier, later);
        }

        if (!IsBlockAncestor(earlierBlock, laterBlock)) return false;

        var childInEarlierBlock = FindDirectChildOperationContainingSpan(earlierBlock.Operations, laterBlock.Syntax.Span);
        if (childInEarlierBlock == null) return false;
        if (earlier.Syntax.SpanStart >= childInEarlierBlock.Syntax.SpanStart) return false;

        return !HasTerminatorBetween(earlierBlock.Operations, earlier.Syntax.SpanStart, childInEarlierBlock.Syntax.SpanStart, later.Syntax);
    }

    private static bool IsNestedUnderOptionalControlFlow(IOperation operation, IBlockOperation enclosingBlock, IOperation later)
    {
        for (var ancestor = operation.Syntax.Parent; ancestor != null && !ReferenceEquals(ancestor, enclosingBlock.Syntax); ancestor = ancestor.Parent)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (IfStatementMakesBranchMandatory(ifStatement, operation.Syntax, later.Syntax))
                    continue;

                return true;
            }

            if (ancestor.IsKind(SyntaxKind.ElseClause))
                continue;

            if (
                ancestor.IsKind(SyntaxKind.SwitchStatement) ||
                ancestor.IsKind(SyntaxKind.SwitchSection) ||
                ancestor.IsKind(SyntaxKind.WhileStatement) ||
                ancestor.IsKind(SyntaxKind.DoStatement) ||
                ancestor.IsKind(SyntaxKind.ForStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachStatement) ||
                ancestor.IsKind(SyntaxKind.ForEachVariableStatement) ||
                ancestor.IsKind(SyntaxKind.ConditionalExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IfStatementMakesBranchMandatory(
        IfStatementSyntax ifStatement,
        SyntaxNode operationSyntax,
        SyntaxNode laterSyntax)
    {
        if (laterSyntax.SpanStart <= ifStatement.Span.End) return false;

        var operationStart = operationSyntax.SpanStart;
        if (ifStatement.Statement.Span.Contains(operationStart))
            return ifStatement.Else?.Statement is { } elseStatement && StatementSkipsLater(elseStatement, laterSyntax);

        if (ifStatement.Else?.Statement.Span.Contains(operationStart) == true)
            return StatementSkipsLater(ifStatement.Statement, laterSyntax);

        return false;
    }

    private static bool StatementSkipsLater(StatementSyntax statement, SyntaxNode laterSyntax)
    {
        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
                return true;

            case BlockSyntax block:
                return block.Statements.Count > 0 && StatementSkipsLater(block.Statements[block.Statements.Count - 1], laterSyntax);

            case IfStatementSyntax ifStatement:
                return ifStatement.Else?.Statement is { } elseStatement &&
                       StatementSkipsLater(ifStatement.Statement, laterSyntax) &&
                       StatementSkipsLater(elseStatement, laterSyntax);

            case BreakStatementSyntax breakStatement:
                return BranchSkipsLater(breakStatement, laterSyntax, includeSwitch: true);

            case ContinueStatementSyntax continueStatement:
                return BranchSkipsLater(continueStatement, laterSyntax, includeSwitch: false);

            default:
                return false;
        }
    }

    private static bool BranchSkipsLater(StatementSyntax branchStatement, SyntaxNode laterSyntax, bool includeSwitch)
    {
        var enclosingConstruct = branchStatement.Ancestors().FirstOrDefault(a =>
            a.IsKind(SyntaxKind.WhileStatement) ||
            a.IsKind(SyntaxKind.DoStatement) ||
            a.IsKind(SyntaxKind.ForStatement) ||
            a.IsKind(SyntaxKind.ForEachStatement) ||
            a.IsKind(SyntaxKind.ForEachVariableStatement) ||
            (includeSwitch && a.IsKind(SyntaxKind.SwitchStatement)));

        return enclosingConstruct != null &&
               laterSyntax.AncestorsAndSelf().Contains(enclosingConstruct) &&
               laterSyntax.SpanStart > branchStatement.SpanStart;
    }

    private static bool HasReattachInRange(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        TextSpan range,
        IOperation save)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches)) return false;

        for (var i = 0; i < reattaches.Count; i++)
        {
            var entry = reattaches[i];
            if (!range.Contains(entry.Span)) continue;

            if (entry.ContextSymbol != null &&
                SymbolEqualityComparer.Default.Equals(entry.ContextSymbol, saveContext) &&
                !HasInterveningDetach(scan, local, saveContext, entry.SpanStart, save))
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

    private static bool BlockReaches(IOperation start, IOperation saveChanges)
    {
        // Local functions and lambdas are separate executable roots; do not let a mutation
        // inside one reach a SaveChanges in the enclosing method.
        if (!start.SharesOwningExecutableRoot(saveChanges)) return false;

        var startSyntax = start.Syntax;
        var saveSyntax = saveChanges.Syntax;
        if (saveSyntax.SpanStart <= startSyntax.SpanStart) return false;

        var startBlock = FindEnclosingBlock(start);
        var saveBlock = FindEnclosingBlock(saveChanges);
        if (startBlock == null || saveBlock == null) return true;

        if (ReferenceEquals(startBlock, saveBlock))
        {
            return !HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, saveSyntax.SpanStart, saveSyntax);
        }

        // Mutation in a nested block, SaveChanges in an ancestor block (e.g., mutation inside
        // an `if`/`using`/`while` body, save after that statement). Walk up the block chain and
        // verify every intermediate block can fall through from the mutation path to the save.
        if (IsBlockAncestor(saveBlock, startBlock))
        {
            var currentBlock = startBlock;
            while (currentBlock != null && !ReferenceEquals(currentBlock, saveBlock))
            {
                var afterSpan = ReferenceEquals(currentBlock, startBlock)
                    ? startSyntax.SpanStart
                    : currentBlock.Syntax.Span.Start;

                if (HasTerminatorBetween(currentBlock.Operations, afterSpan, currentBlock.Syntax.Span.End, saveSyntax))
                    return false;

                var parentBlock = FindEnclosingBlock(currentBlock);
                if (parentBlock == null) return false;

                var childInParent = FindDirectChildOperationContainingSpan(parentBlock.Operations, currentBlock.Syntax.Span);
                if (childInParent == null) return false;

                var beforeSpan = ReferenceEquals(parentBlock, saveBlock)
                    ? saveSyntax.SpanStart
                    : parentBlock.Syntax.Span.End;

                if (HasTerminatorBetween(parentBlock.Operations, childInParent.Syntax.Span.End, beforeSpan, saveSyntax))
                    return false;

                currentBlock = parentBlock;
            }

            return true;
        }

        // SaveChanges in a nested block, mutation in an ancestor block (e.g., mutation at the
        // top level, save inside a conditional branch that follows it).
        if (IsBlockAncestor(startBlock, saveBlock))
        {
            var containingOperation = FindDirectChildOperationContainingSpan(startBlock.Operations, saveBlock.Syntax.Span);
            if (containingOperation == null) return false;

            if (HasTerminatorBetween(startBlock.Operations, startSyntax.SpanStart, containingOperation.Syntax.SpanStart, saveSyntax))
                return false;

            return !HasTerminatorBetween(saveBlock.Operations, saveBlock.Syntax.Span.Start, saveSyntax.SpanStart, saveSyntax);
        }

        // The blocks are siblings under a common ancestor (different branches of an `if`,
        // separate `case` labels, etc.) — there is no guaranteed path from one to the other.
        return false;
    }

    private static bool IsBlockAncestor(IBlockOperation ancestor, IBlockOperation descendant)
    {
        var current = descendant.Parent;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current.Parent;
        }

        return false;
    }

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

    private static IOperation? FindDirectChildOperationContainingSpan(
        System.Collections.Immutable.ImmutableArray<IOperation> operations,
        TextSpan span)
    {
        IOperation? best = null;
        foreach (var operation in operations)
        {
            if (!operation.Syntax.Span.Contains(span.Start)) continue;
            if (best == null || operation.Syntax.Span.Length < best.Syntax.Span.Length)
                best = operation;
        }

        return best;
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

        // The last tracking directive applied wins (each AsTracking/AsNoTracking overwrites the
        // query's QueryTrackingBehavior). Walking up the receiver chain, the first directive
        // encountered is the one applied last, so it decides the effective mode: a trailing
        // AsTracking() (AsNoTracking().AsTracking()) makes the entity tracked, so the mutation
        // persists and LC044 must not fire. Keep walking to the root receiver regardless.
        bool? effectiveNoTracking = null;

        while (current is IInvocationOperation inv)
        {
            if (effectiveNoTracking is null)
            {
                if (inv.TargetMethod.Name == "AsNoTracking")
                    effectiveNoTracking = true;
                else if (inv.TargetMethod.Name == "AsTracking")
                    effectiveNoTracking = false;
            }

            var next = inv.GetInvocationReceiver();
            if (next == null)
            {
                rootReceiver = inv;
                break;
            }

            current = next.UnwrapConversions();
        }

        if (effectiveNoTracking != true) return false;

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
