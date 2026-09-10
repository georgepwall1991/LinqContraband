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
            scan, root, context.Compilation, local, saveContext, declSpan, saveSpan, save);
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
            var mutationNullable = FindFirstUnpersistedForeachMutation(
                scan,
                root,
                context.Compilation,
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
        IOperation root,
        Compilation compilation,
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
            // Mutations inside a local function are positioned at the declaration,
            // not at execution: the range filter applies to the lifted invocation
            // instead, so declaration-after-save callees still report.
            var inLocalFunction = entry.Operation.Syntax.Ancestors()
                .OfType<LocalFunctionStatementSyntax>()
                .Any();
            if (!inLocalFunction && (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan)) continue;
            // A mutation in a handler with a constant-false filter never executes.
            if (ImpossibleCatchEncloses(entry, root.Syntax)) continue;
            var effectiveOperation = entry.Operation;
            if (!BlockReaches(effectiveOperation, save))
            {
                // Persistence and earlier-save gates are evaluated per
                // invocation: an earlier call may be saved while a later one
                // is lost, so the first surviving invocation reports.
                if (!TryLiftLocalFunctionMutation(
                        scan, local, saveContext, entry, root, compilation, afterSpan, beforeSpan, save, out var lifted) ||
                    lifted == null)
                {
                    continue;
                }

                var liftedHit = false;
                foreach (var invocation in lifted)
                {
                    if (HasEarlierSaveChangesOnSameContext(scan, saveContext, invocation, save))
                        continue;
                    if (HasDominatingPriorReattach(
                            scan,
                            local,
                            saveContext,
                            entry.ReceiverPath,
                            afterSpan,
                            invocation,
                            save))
                    {
                        continue;
                    }
                    if (HasReattach(
                            scan,
                            local,
                            saveContext,
                            entry.ReceiverPath,
                            invocation,
                            save))
                    {
                        continue;
                    }

                    best = EarlierMutation(best, entry, invocation);
                    liftedHit = true;
                    break;
                }

                if (!liftedHit)
                    continue;

                continue;
            }

            if (HasEarlierSaveChangesOnSameContext(scan, saveContext, effectiveOperation, save))
                continue;
            if (HasDominatingPriorReattach(
                    scan,
                    local,
                    saveContext,
                    entry.ReceiverPath,
                    afterSpan,
                    effectiveOperation,
                    save))
            {
                continue;
            }
            if (HasReattach(
                    scan,
                    local,
                    saveContext,
                    entry.ReceiverPath,
                    effectiveOperation,
                    save))
            {
                continue;
            }

            best = EarlierMutation(best, entry, effectiveOperation);
        }

        return best;
    }

    private static MutationHit? FindFirstUnpersistedForeachMutation(
        AsNoTrackingThenModifyRootScan scan,
        IOperation root,
        Compilation compilation,
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
            var inLocalFunction = entry.Operation.Syntax.Ancestors()
                .OfType<LocalFunctionStatementSyntax>()
                .Any();
            if (!inLocalFunction && (entry.SpanStart <= afterSpan || entry.SpanStart >= beforeSpan)) continue;
            if (ImpossibleCatchEncloses(entry, root.Syntax)) continue;
            var effectiveOperation = entry.Operation;
            if (!BlockReaches(effectiveOperation, save))
            {
                if (!TryLiftLocalFunctionMutation(
                        scan, local, saveContext, entry, root, compilation, afterSpan, beforeSpan, save, out var lifted) ||
                    lifted == null)
                {
                    continue;
                }

                var liftedHit = false;
                foreach (var invocation in lifted)
                {
                    if (HasEarlierSaveChangesOnSameContext(scan, saveContext, invocation, save))
                        continue;
                    if (HasReattachInRange(
                            scan, local, saveContext, entry.ReceiverPath, invocation, forEachSpan, save))
                    {
                        continue;
                    }

                    best = EarlierMutation(best, entry, invocation);
                    liftedHit = true;
                    break;
                }

                if (!liftedHit)
                    continue;

                continue;
            }

            if (HasEarlierSaveChangesOnSameContext(scan, saveContext, effectiveOperation, save))
                continue;
            if (HasReattachInRange(
                    scan, local, saveContext, entry.ReceiverPath, effectiveOperation, forEachSpan, save))
            {
                continue;
            }

            best = EarlierMutation(best, entry, effectiveOperation);
        }

        return best;
    }

    private static MutationHit EarlierMutation(
        MutationHit? current,
        MutationEntry candidate,
        IOperation effectiveOperation)
    {
        if (current == null || effectiveOperation.Syntax.SpanStart < current.Value.Operation.Syntax.SpanStart)
        {
            return new MutationHit(
                effectiveOperation,
                candidate.TargetLocation,
                candidate.PropertyName,
                candidate.ReceiverPath);
        }

        return current.Value;
    }

    private static bool NestedInvalidatedAfter(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        int afterSpan,
        LocalFunctionStatementSyntax nested,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            foreach (var detach in detaches)
            {
                if (!nested.Span.Contains(detach.Operation.Syntax.Span))
                    continue;
                if (detach.SpanStart <= afterSpan)
                    continue;
                if (!IsDirectNestedOperation(detach.Operation.Syntax, nested))
                    continue;
                if (detach.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(detach.ContextSymbol, saveContext))
                    continue;
                if (detach.TargetPath.Length != entry.ReceiverPath.Length ||
                    !MemberPathIsPrefix(detach.TargetPath, entry.ReceiverPath))
                    continue;
                if (DeadGuardEncloses(
                        detach.Operation.Syntax, detach.SpanStart, model, localFunctionSyntax))
                    continue;
                return true;
            }
        }

        foreach (var clear in scan.TrackerClears)
        {
            if (!nested.Span.Contains(clear.Operation.Syntax.Span))
                continue;
            if (clear.SpanStart <= afterSpan)
                continue;
            if (!IsDirectNestedOperation(clear.Operation.Syntax, nested))
                continue;
            if (clear.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(clear.ContextSymbol, saveContext))
                continue;
            if (DeadGuardEncloses(
                    clear.Operation.Syntax, clear.SpanStart, model, localFunctionSyntax))
                continue;
            return true;
        }

        return false;
    }

    private static bool IsDirectNestedOperation(SyntaxNode node, LocalFunctionStatementSyntax nested)
    {
        var boundary = node.Ancestors().FirstOrDefault(ancestor =>
            ancestor is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        return ReferenceEquals(boundary, nested);
    }

    private static bool CalleePersistsThroughSave(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        IOperation save,
        LocalFunctionStatementSyntax localFunctionSyntax,
        Compilation compilation,
        IInvocationOperation candidate,
        IOperation root)
    {
        // Callee-side persistence (direct, catch, or nested-helper reattachment)
        // only voids the lift when nothing in the caller untracks the entity
        // between this call and the save: a later detach or clear loses the
        // mutation despite the earlier persistence.
        if (!CalleeReattachPersistsMutation(scan, local, saveContext, entry, localFunctionSyntax)
            && !CalleeCatchPersistsMutation(scan, local, saveContext, entry, localFunctionSyntax)
            && !NestedHelperReattaches(scan, local, saveContext, entry, localFunctionSyntax, compilation))
            return false;
        return !CallerInvalidationBetween(
            scan, local, saveContext, entry,
            candidate.Syntax.SpanStart, save.Syntax.SpanStart,
            candidate.Syntax, candidate.Syntax.SpanStart, root, compilation);
    }

    private static bool CallerHelperTracksThroughSave(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        IOperation root,
        Compilation compilation,
        IInvocationOperation outerCandidate,
        IOperation save,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A sibling helper invoked in the caller that leaves the entity tracked
        // persists the callee's mutation exactly like a caller-side Attach. The
        // invocation must dominate the call (attach) or the save (persisting
        // reattachment), with no invalidation on the way to the save.
        var model = compilation.GetSemanticModel(localFunctionSyntax.SyntaxTree);
        // The caller is usually the method body, but it can itself be a local
        // function or lambda: caller-level means directly in the caller's own
        // executable root, not necessarily the method's.
        var callerExecutable = root.Syntax.AncestorsAndSelf().FirstOrDefault(ancestor =>
            ancestor is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        var scope = localFunctionSyntax.Ancestors().OfType<BlockSyntax>().LastOrDefault();
        var helpers = scope != null
            ? scope.DescendantNodes().OfType<LocalFunctionStatementSyntax>()
            : localFunctionSyntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>();
        var outerSpan = outerCandidate.Syntax.SpanStart;
        var saveSpan = save.Syntax.SpanStart;
        foreach (var helper in helpers)
        {
            if (helper == localFunctionSyntax)
                continue;
            if (localFunctionSyntax.Span.Contains(helper.Span))
                continue;
            if (model.GetDeclaredSymbol(helper) is not IMethodSymbol helperSymbol)
                continue;
            HelperLeavesTracked(
                scan, local, saveContext, entry, helper, model,
                out var hasAttach, out var hasPersisting);
            if (!hasAttach && !hasPersisting)
                continue;
            foreach (var helperInvocation in root.Descendants().OfType<IInvocationOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        helperInvocation.TargetMethod.OriginalDefinition,
                        helperSymbol.OriginalDefinition))
                    continue;
                if (localFunctionSyntax.Span.Contains(helperInvocation.Syntax.Span))
                    continue;
                if (!IsDirectCallerOperation(helperInvocation.Syntax, callerExecutable))
                    continue;
                var invocationModel = compilation.GetSemanticModel(helperInvocation.Syntax.SyntaxTree);
                if (DeadGuardEncloses(
                        helperInvocation.Syntax, helperInvocation.Syntax.SpanStart,
                        invocationModel, root.Syntax))
                    continue;
                if (hasAttach
                    && helperInvocation.Syntax.SpanStart < outerSpan
                    && Dominates(helperInvocation, outerCandidate)
                    && !CallerInvalidationBetween(
                        scan, local, saveContext, entry,
                        helperInvocation.Syntax.SpanStart, saveSpan,
                        outerCandidate.Syntax, outerSpan, root, compilation))
                    return true;
                if (hasPersisting
                    && helperInvocation.Syntax.SpanStart < saveSpan
                    && Dominates(helperInvocation, save)
                    && !CallerInvalidationBetween(
                        scan, local, saveContext, entry,
                        helperInvocation.Syntax.SpanStart, saveSpan,
                        outerCandidate.Syntax, outerSpan, root, compilation))
                    return true;
            }
        }

        return false;
    }

    private static void HelperLeavesTracked(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax helper,
        SemanticModel? model,
        out bool hasAttach,
        out bool hasPersisting)
    {
        // A helper leaves the entity tracked when it reattaches it on every
        // path: the reattachment runs straight-line in the helper body (block
        // or expression body) and no later invalidation inside the helper
        // undoes it.
        hasAttach = false;
        hasPersisting = false;
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return;
        if (helper.Body == null && helper.ExpressionBody == null)
            return;
        // Calling an iterator never executes its body; enumeration does.
        if (helper.DescendantNodes().OfType<YieldStatementSyntax>().Any())
            return;
        foreach (var candidate in reattaches)
        {
            var syntax = candidate.Operation.Syntax;
            if (!helper.Span.Contains(syntax.Span))
                continue;
            if (candidate.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContextSymbol, saveContext))
                continue;
            if (!ReattachCoversPath(candidate, entry.ReceiverPath))
                continue;
            if (!IsDirectNestedOperation(syntax, helper))
                continue;
            if (helper.Body != null && !IsUnconditionalWithin(syntax, helper.Body))
                continue;
            if (DeadGuardEncloses(syntax, syntax.SpanStart, model, helper))
                continue;
            if (NestedInvalidatedAfter(
                    scan, local, saveContext, entry, syntax.SpanStart, helper, model, helper))
                continue;
            if (candidate.PersistsExistingMutation)
                hasPersisting = true;
            else
                hasAttach = true;
        }
    }

    private static bool CallerInvalidationBetween(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        int fromSpan,
        int toSpan,
        SyntaxNode mutationSyntax,
        int mutationSpan,
        IOperation root,
        Compilation compilation)
    {
        var model = compilation.GetSemanticModel(root.Syntax.SyntaxTree);
        // See CallerHelperTracksThroughSave: the caller root decides what counts
        // as a caller-level operation.
        var callerExecutable = root.Syntax.AncestorsAndSelf().FirstOrDefault(ancestor =>
            ancestor is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            foreach (var detach in detaches)
            {
                var syntax = detach.Operation.Syntax;
                if (detach.SpanStart <= fromSpan || detach.SpanStart >= toSpan)
                    continue;
                if (!root.Syntax.Span.Contains(syntax.Span))
                    continue;
                if (!IsDirectCallerOperation(syntax, callerExecutable))
                    continue;
                if (detach.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(detach.ContextSymbol, saveContext))
                    continue;
                if (detach.TargetPath.Length != entry.ReceiverPath.Length ||
                    !MemberPathIsPrefix(detach.TargetPath, entry.ReceiverPath))
                    continue;
                if (BranchesAreMutuallyExclusive(syntax, mutationSpan, root.Syntax))
                    continue;
                if (CallerInvalidationDiverted(syntax, model, root.Syntax))
                    continue;
                if (MutationPathSkipsInvalidation(mutationSyntax, mutationSpan, syntax, model, root.Syntax))
                    continue;
                if (DeadGuardEncloses(syntax, detach.SpanStart, model, root.Syntax))
                    continue;
                return true;
            }
        }

        foreach (var clear in scan.TrackerClears)
        {
            var syntax = clear.Operation.Syntax;
            if (clear.SpanStart <= fromSpan || clear.SpanStart >= toSpan)
                continue;
            if (!root.Syntax.Span.Contains(syntax.Span))
                continue;
            if (!IsDirectCallerOperation(syntax, callerExecutable))
                continue;
            if (clear.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(clear.ContextSymbol, saveContext))
                continue;
            if (BranchesAreMutuallyExclusive(syntax, mutationSpan, root.Syntax))
                continue;
            if (CallerInvalidationDiverted(syntax, model, root.Syntax))
                continue;
            if (MutationPathSkipsInvalidation(mutationSyntax, mutationSpan, syntax, model, root.Syntax))
                continue;
            if (DeadGuardEncloses(syntax, clear.SpanStart, model, root.Syntax))
                continue;
            return true;
        }

        // An invoked helper between the two points voids persistence when the
        // helper itself can untrack the entity: its body carries a detach or
        // clear that no later reattachment inside the same helper undoes. A
        // helper that leaves the entity tracked is already vetted above.
        foreach (var helperInvocation in root.Descendants().OfType<IInvocationOperation>())
        {
            var invocationSyntax = helperInvocation.Syntax;
            if (invocationSyntax.SpanStart <= fromSpan || invocationSyntax.SpanStart >= toSpan)
                continue;
            if (!IsDirectCallerOperation(invocationSyntax, callerExecutable))
                continue;
            if (DeadGuardEncloses(invocationSyntax, invocationSyntax.SpanStart, model, root.Syntax))
                continue;
            if (CallerInvalidationDiverted(invocationSyntax, model, root.Syntax))
                continue;
            if (BranchesAreMutuallyExclusive(invocationSyntax, mutationSpan, root.Syntax))
                continue;
            var helper = helperInvocation.TargetMethod.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<LocalFunctionStatementSyntax>()
                .FirstOrDefault();
            if (helper == null)
                continue;
            HelperLeavesTracked(
                scan, local, saveContext, entry, helper, model,
                out var helperAttach, out var helperPersisting);
            if (helperAttach || helperPersisting)
                continue;
            if (HelperCarriesInvalidation(scan, local, saveContext, entry, helper, model))
                return true;
        }

        return false;
    }

    private static bool HelperCarriesInvalidation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax helper,
        SemanticModel? model)
    {
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            foreach (var detach in detaches)
            {
                var syntax = detach.Operation.Syntax;
                if (!helper.Span.Contains(syntax.Span))
                    continue;
                if (!IsDirectNestedOperation(syntax, helper))
                    continue;
                if (detach.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(detach.ContextSymbol, saveContext))
                    continue;
                if (detach.TargetPath.Length != entry.ReceiverPath.Length ||
                    !MemberPathIsPrefix(detach.TargetPath, entry.ReceiverPath))
                    continue;
                if (DeadGuardEncloses(syntax, detach.SpanStart, model, helper))
                    continue;
                return true;
            }
        }

        foreach (var clear in scan.TrackerClears)
        {
            var syntax = clear.Operation.Syntax;
            if (!helper.Span.Contains(syntax.Span))
                continue;
            if (!IsDirectNestedOperation(syntax, helper))
                continue;
            if (clear.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(clear.ContextSymbol, saveContext))
                continue;
            if (DeadGuardEncloses(syntax, clear.SpanStart, model, helper))
                continue;
            return true;
        }

        return false;
    }

    private static bool CallerInvalidationDiverted(
        SyntaxNode invalidation,
        SemanticModel? model,
        SyntaxNode boundary)
    {
        // Caller-side mirror of InvalidationCannotReachMutation without the
        // callee-only pre/post asymmetry: a return in the caller diverts past
        // the save (or the mutation) on every path, so an invalidation in a
        // terminating arm never voids persistence.
        foreach (var ifStatement in invalidation.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!boundary.Span.Contains(ifStatement.Span))
                break;
            var which = BranchArm(ifStatement, invalidation.SpanStart);
            if (which == 0)
                continue;
            var arm = which == 1 ? ifStatement.Statement : ifStatement.Else?.Statement;
            if (arm == null)
                continue;
            var last = arm is BlockSyntax armBlock && armBlock.Statements.Count > 0
                ? armBlock.Statements[armBlock.Statements.Count - 1]
                : arm;
            if (last is ReturnStatementSyntax || StatementNeverCompletesNormally(last, model))
                return true;
        }

        return false;
    }

    private static bool IsDirectCallerOperation(SyntaxNode node, SyntaxNode? callerExecutable)
    {
        // Caller-level operations sit directly in the caller's own executable
        // root: anything nested in a deeper local function or lambda belongs
        // to that root instead.
        var boundary = node.Ancestors().FirstOrDefault(ancestor =>
            ancestor is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        return ReferenceEquals(boundary, callerExecutable);
    }

    private static bool CallerInvocationUnreachable(
        SyntaxNode invocationSyntax,
        SyntaxNode rootSyntax,
        SemanticModel? model)
    {
        var body = rootSyntax as BlockSyntax
            ?? (rootSyntax as MethodDeclarationSyntax)?.Body
            ?? (rootSyntax as LocalFunctionStatementSyntax)?.Body;
        if (body == null)
            return false;
        // Labels or gotos in the body's own executable defeat span-order
        // reachability: control may land past the diverter.
        foreach (var node in body.DescendantNodes())
        {
            if (node is not LabeledStatementSyntax and not GotoStatementSyntax)
                continue;
            var boundary = node.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (boundary == null || !body.Span.Contains(boundary.Span))
                return false;
        }

        foreach (var statement in body.Statements)
        {
            if (statement.SpanStart >= invocationSyntax.SpanStart)
                break;
            if (statement is ReturnStatementSyntax or ThrowStatementSyntax ||
                StatementNeverCompletesNormally(statement, model))
                return true;
        }

        return false;
    }

    private static bool TryLiftLocalFunctionMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        IOperation root,
        Compilation compilation,
        int afterSpan,
        int beforeSpan,
        IOperation save,
        out List<IInvocationOperation>? lifted)
    {
        lifted = null;
        var localFunctionSyntax = entry.Operation.Syntax.Ancestors()
            .OfType<LocalFunctionStatementSyntax>()
            .FirstOrDefault();
        if (localFunctionSyntax == null)
            return false;

        // The mutation's owning executable must be the local function itself. A
        // mutation inside a lambda nested in the function never runs when the
        // function is invoked unless the delegate is invoked too.
        var boundary = entry.Operation.Syntax.Ancestors()
            .FirstOrDefault(node => node is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        if (!ReferenceEquals(boundary, localFunctionSyntax))
            return false;

        // Calling an iterator never executes its body; enumeration does.
        if (localFunctionSyntax.DescendantNodes().OfType<YieldStatementSyntax>().Any())
            return false;

        // The invocation reaches the save only if the callee can return normally
        // after the mutation, and the mutation itself must be reachable and not
        if (ThrowFollowsMutationInBlock(entry, localFunctionSyntax))
            return false;
        if (TerminatorPrecedesMutation(entry, localFunctionSyntax))
            return false;
        if (EnclosingLoopNeverExits(entry, localFunctionSyntax))
            return false;
        if (EmptyTryCatchEncloses(entry, localFunctionSyntax))
            return false;
        // A mutation in a handler with a constant-false filter never executes.
        if (ImpossibleCatchEncloses(entry, localFunctionSyntax))
            return false;
        if (UnreachableGuardEncloses(entry, localFunctionSyntax))
            return false;


        // The callee's own save already reports this mutation; lifting would
        // duplicate the diagnostic at the caller's save.
        if (CalleeSavesAfterMutation(scan, local, saveContext, entry, localFunctionSyntax))
            return false;
        // A same-context save inside the callee that the mutation reaches with
        // tracking intact writes the change: later untracking cannot undo a
        // completed write, so the outer save has nothing left to lose.
        if (CalleeInnerSavePersists(scan, local, saveContext, entry, localFunctionSyntax))
            return false;

        var model = compilation.GetSemanticModel(entry.Operation.Syntax.SyntaxTree);
        if (model.GetDeclaredSymbol(localFunctionSyntax) is not IMethodSymbol functionSymbol)
            return false;

        var eligible = new List<IInvocationOperation>();
        foreach (var candidate in root.Descendants().OfType<IInvocationOperation>())
        {
            var invocationSyntax = candidate.Syntax;
            if (localFunctionSyntax.Span.Contains(invocationSyntax.Span))
                continue;
            if (invocationSyntax.SpanStart <= afterSpan || invocationSyntax.SpanStart >= beforeSpan)
                continue;
            // An invocation in a constant-dead branch never executes: it cannot
            // carry the mutation to the save.
            if (DeadGuardEncloses(
                    invocationSyntax, invocationSyntax.SpanStart,
                    compilation.GetSemanticModel(invocationSyntax.SyntaxTree), root.Syntax))
                continue;
            // An invocation after an unconditional diverter at the caller body
            // level never executes either (with no labels or gotos to reroute
            // control around it).
            if (CallerInvocationUnreachable(
                    invocationSyntax, root.Syntax,
                    compilation.GetSemanticModel(invocationSyntax.SyntaxTree)))
                continue;
            if (!SymbolEqualityComparer.Default.Equals(
                    candidate.TargetMethod.OriginalDefinition,
                    functionSymbol.OriginalDefinition))
            {
                continue;
            }

            if (functionSymbol.IsAsync && !IsAwaited(candidate))
                continue;
            if (!BlockReaches(candidate, save))
                continue;
            // The callee leaves the entity tracked and nothing in the caller
            // untracks it between this call and the save: the mutation persists.
            if (CalleePersistsThroughSave(
                    scan, local, saveContext, entry, save, localFunctionSyntax,
                    compilation, candidate, root))
                continue;
            // A sibling helper invoked before this call leaves the entity
            // tracked through the save: same persistence, caller-side.
            if (CallerHelperTracksThroughSave(
                    scan, local, saveContext, entry, root, compilation,
                    candidate, save, localFunctionSyntax))
                continue;
            eligible.Add(candidate);
        }

        eligible.Sort(static (left, right) => left.Syntax.SpanStart.CompareTo(right.Syntax.SpanStart));
        lifted = eligible.Count > 0 ? eligible : null;
        return lifted != null;
    }

    private static bool ThrowFollowsMutationInBlock(
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A reachable return after the mutation lets the caller save the
        // unpersisted change: no later terminator can void the lift. (Returns
        // in dead branches are ignored, as is unreachable code after a throw.)
        var mutationSpan = entry.Operation.Syntax.SpanStart;
        if (CalleeReturnsAfterMutation(entry, mutationSpan, localFunctionSyntax))
            return false;

        var node = entry.Operation.Syntax;
        foreach (var block in node.Ancestors().OfType<BlockSyntax>())
        {
            // Ancestors continue past the callee into the caller; only the callee's
            // own blocks decide whether the invocation can return normally.
            if (!localFunctionSyntax.Span.Contains(block.Span))
                break;

            // A throw inside a try block with a resuming handler may be caught:
            // the callee can still return normally, so this level proves no
            // termination.
            if (block.Parent is TryStatementSyntax tryParent
                && tryParent.Block == block
                && TryBlockMayResume(entry, tryParent))
            {
                node = block;
                continue;
            }

            var passedPath = false;
            foreach (var statement in block.Statements)
            {
                if (!passedPath)
                {
                    if (statement.Span.Contains(mutationSpan) || statement.Span.Contains(node.SpanStart))
                        passedPath = true;
                    continue;
                }

                if (StatementNeverCompletesNormally(statement, entry.Operation.SemanticModel))
                    return true;
            }

            node = block;
        }

        // A finally that never completes normally propagates out of every
        // enclosed path: the invocation cannot return after the mutation.
        foreach (var tryStatement in entry.Operation.Syntax.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(tryStatement.Span))
                break;

            if (tryStatement.Finally?.Block is { } finallyBlock
                && finallyBlock.Statements.Count > 0
                && StatementNeverCompletesNormally(
                    finallyBlock.Statements[finallyBlock.Statements.Count - 1], entry.Operation.SemanticModel))
                return true;
        }

        return false;
    }
    private static bool CalleeReturnsAfterMutation(
        MutationEntry entry,
        int mutationSpan,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A reachable return after the mutation lets the caller save the
        // unpersisted change. Returns in dead branches, nested executables,
        // or after an unconditional terminator never run.
        var model = entry.Operation.SemanticModel;
        foreach (var ret in localFunctionSyntax.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (ret.SpanStart <= mutationSpan)
                continue;
            var boundary = ret.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (!ReferenceEquals(boundary, localFunctionSyntax))
                continue;
            if (DeadGuardEncloses(ret, ret.SpanStart, model, localFunctionSyntax))
                continue;
            // A return on a path mutually exclusive with the mutation's path
            // never runs after the mutation executed.
            if (BranchesAreMutuallyExclusive(ret, mutationSpan, localFunctionSyntax))
                continue;
            if (ReturnUnreachable(ret, mutationSpan, model, localFunctionSyntax))
                continue;
            // A terminating finally overrides the return: the invocation
            // throws instead of returning to the caller. A returned expression
            // that always throws never returns either.
            if (ReturnSuppressedByFinally(ret, model, localFunctionSyntax))
                continue;
            if (ret.Expression != null && ExpressionAlwaysThrows(ret.Expression, model))
                continue;
            return true;
        }

        return false;
    }

    private static bool ReturnSuppressedByFinally(
        ReturnStatementSyntax ret,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        foreach (var tryStatement in ret.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(tryStatement.Span))
                break;
            if (tryStatement.Finally?.Block is { } finallyBlock
                && finallyBlock.Statements.Count > 0
                && StatementNeverCompletesNormally(
                    finallyBlock.Statements[finallyBlock.Statements.Count - 1], model))
                return true;
        }

        return false;
    }


    private static bool ReturnUnreachable(
        ReturnStatementSyntax ret,
        int mutationSpan,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        var node = (SyntaxNode)ret;
        foreach (var block in ret.Ancestors().OfType<BlockSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(block.Span))
                break;
            foreach (var statement in block.Statements)
            {
                if (statement.Span.Contains(node.SpanStart))
                    break;
                if (statement.SpanStart <= mutationSpan)
                    continue;
                if (statement is GotoStatementSyntax
                    or BreakStatementSyntax
                    or ContinueStatementSyntax
                    || PreMutationTerminates(statement, model))
                    return true;
            }

            node = block;
        }

        return false;
    }


    private static bool TryBlockMayResume(MutationEntry entry, TryStatementSyntax tryParent)
    {
        // Every throw after the mutation's path inside the try block must land
        // in a handler that completes normally. Handlers apply in order: the
        // first matching clause decides, so a rethrowing specific handler
        // shadows a completing general one below it.
        var mutationSpan = entry.Operation.Syntax.SpanStart;
        var model = entry.Operation.SemanticModel;
        var passedPath = false;
        foreach (var statement in tryParent.Block.Statements)
        {
            if (!passedPath)
            {
                if (statement.Span.Contains(mutationSpan))
                    passedPath = true;
                continue;
            }

            // An exitless loop after the path hangs before any handler runs.
            // (Throws resolve below; returns still resume the caller.)
            if (LoopNeverExits(statement))
                return false;
        }

        foreach (var throwStatement in tryParent.Block.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            if (throwStatement.SpanStart <= mutationSpan)
                continue;
            if (throwStatement.Ancestors().Any(ancestor =>
                    ancestor is LambdaExpressionSyntax
                        or AnonymousMethodExpressionSyntax
                        or LocalFunctionStatementSyntax
                    && tryParent.Span.Contains(ancestor.Span)))
                continue;
            if (!ThrowResumes(throwStatement, tryParent, model))
                return false;
        }

        return true;
    }

    private static bool ThrowResumes(
        ThrowStatementSyntax throwStatement,
        TryStatementSyntax tryParent,
        SemanticModel? model)
    {
        var thrown = throwStatement.Expression != null
            ? model?.GetTypeInfo(throwStatement.Expression).Type
            : null;
        foreach (var catchClause in tryParent.Catches)
        {
            // Only a proven-true filter matches; anything else cannot be shown
            // to handle the throw.
            if (catchClause.Filter != null
                && ConstantBool(catchClause.Filter.FilterExpression, model) is not true)
                continue;

            if (!CatchMatches(catchClause.Declaration, thrown, model))
                continue;

            // First matching handler decides: it resumes only when its body
            // can complete normally.
            if (catchClause.Block.Statements.Count == 0)
                return true;

            return !StatementNeverCompletesNormally(
                catchClause.Block.Statements[catchClause.Block.Statements.Count - 1], model);
        }

        // No handler catches the throw: it escapes the callee.
        return false;
    }

    private static bool TerminatorPrecedesMutation(
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // Walk outward from the mutation: an unconditional terminator before
        // the mutation's path at any enclosing level means it never executes.
        // Returns and other jumps block execution even though a return after
        // the mutation still lets the caller save.
        var mutationSpan = entry.Operation.Syntax.SpanStart;
        var model = entry.Operation.SemanticModel;
        var node = entry.Operation.Syntax;
        foreach (var block in node.Ancestors().OfType<BlockSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(block.Span))
                break;

            foreach (var statement in block.Statements)
            {
                if (statement.Span.Contains(node.SpanStart))
                    break;
                if (statement is GotoStatementSyntax
                    or BreakStatementSyntax
                    or ContinueStatementSyntax
                    || PreMutationTerminates(statement, model))
                    return true;
            }

            node = block;
        }

        return false;
    }

    private static bool PreMutationTerminates(StatementSyntax statement, SemanticModel? model)
    {
        // Before the mutation, anything that prevents fall-through voids the
        // lift: returns, throws, and compound statements (branches, loops,
        // try blocks) that cannot complete normally. This is stricter than
        // post-mutation analysis, where a return still lets the caller save.
        if (statement is ReturnStatementSyntax)
            return true;

        if (statement is BlockSyntax nested && nested.Statements.Count > 0)
            return PreMutationTerminates(nested.Statements[nested.Statements.Count - 1], model);

        if (statement is IfStatementSyntax conditional
            && conditional.Else?.Statement is { } elseBranch)
            return PreMutationTerminates(conditional.Statement, model)
                && PreMutationTerminates(elseBranch, model);

        if (statement is TryStatementSyntax attempt)
        {
            if (attempt.Finally?.Block is { } cleanup
                && cleanup.Statements.Count > 0
                && PreMutationTerminates(cleanup.Statements[cleanup.Statements.Count - 1], model))
                return true;

            return attempt.Block?.Statements.Count > 0
                && PreMutationTerminates(attempt.Block.Statements[attempt.Block.Statements.Count - 1], model)
                && attempt.Catches.All(catchClause =>
                    catchClause.Block.Statements.Count > 0
                    && PreMutationTerminates(
                        catchClause.Block.Statements[catchClause.Block.Statements.Count - 1], model));
        }

        return StatementNeverCompletesNormally(statement, model);
    }
    private static bool EnclosingLoopNeverExits(
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A mutation trapped in an exitless unconditional loop can never reach
        // the caller's save: the invocation never returns.
        foreach (var loop in entry.Operation.Syntax.Ancestors().OfType<StatementSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(loop.Span))
                break;
            if (LoopNeverExits(loop))
                return true;
        }

        return false;
    }
    private static bool ImpossibleCatchEncloses(
        MutationEntry entry,
        SyntaxNode boundary)
    {
        var model = entry.Operation.SemanticModel;
        foreach (var catchClause in entry.Operation.Syntax.Ancestors().OfType<CatchClauseSyntax>())
        {
            if (!boundary.Span.Contains(catchClause.Span))
                break;
            if (CatchIsImpossible(catchClause, model))
                return true;
        }

        return false;
    }

    private static bool EmptyTryCatchEncloses(
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A mutation in a catch block whose try block is empty never executes:
        // an empty try cannot transfer control into its handlers.
        foreach (var catchClause in entry.Operation.Syntax.Ancestors().OfType<CatchClauseSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(catchClause.Span))
                break;
            if (catchClause.Parent is TryStatementSyntax tryStatement
                && tryStatement.Block.Statements.Count == 0)
                return true;
        }

        return false;
    }




    private static bool CatchMatches(
        CatchDeclarationSyntax? declaration,
        ITypeSymbol? thrown,
        SemanticModel? model)
    {
        // A bare `catch {}` handles everything.
        if (declaration?.Type == null)
            return true;

        var caught = model?.GetTypeInfo(declaration.Type).Type;
        if (caught == null)
            return false;

        // Exception and object handlers match every throw; otherwise the thrown
        // type must equal or derive from the declared type.
        if (caught.SpecialType == SpecialType.System_Object
            || caught.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Exception")
            return true;
        if (thrown == null)
            return false;

        for (var current = thrown; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition, caught.OriginalDefinition))
                return true;
        }

        return thrown.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(
                candidate.OriginalDefinition, caught.OriginalDefinition));
    }

    private static bool TryHandlersTerminate(TryStatementSyntax tryStatement, SemanticModel? model)
    {
        // Every handler terminates (or none exists): failure never falls
        // through, so reaching past the statement implies success.
        foreach (var catchClause in tryStatement.Catches)
        {
            if (CatchIsImpossible(catchClause, model))
                continue;
            if (catchClause.Block.Statements.Count == 0)
                return false;

            var last = catchClause.Block.Statements[catchClause.Block.Statements.Count - 1];
            if (last is not ReturnStatementSyntax
                && !StatementNeverCompletesNormally(last, model))
                return false;
        }

        return true;
    }

    private static bool LoopNeverExits(StatementSyntax statement)
    {
        // An unconditional loop (`while (true)`, `for (;;)`) without a
        // reachable exit never returns to the caller.
        StatementSyntax? body = statement switch
        {
            WhileStatementSyntax whileLoop when whileLoop.Condition?.IsKind(SyntaxKind.TrueLiteralExpression) == true => whileLoop.Statement,
            ForStatementSyntax forLoop when forLoop.Condition == null || forLoop.Condition.IsKind(SyntaxKind.TrueLiteralExpression) => forLoop.Statement,
            DoStatementSyntax doLoop when doLoop.Condition?.IsKind(SyntaxKind.TrueLiteralExpression) == true => doLoop.Statement,
            _ => null,
        };

        return body != null && !LoopBodyCanExit(body, inSwitch: false, breakExits: true);
    }

    private static bool LoopBodyCanExit(SyntaxNode node, bool inSwitch, bool breakExits)
    {
        // Returns and gotos always divert the callee path. A break exits only
        // when it targets our loop: breaks inside nested loops target the
        // inner loop, and breaks inside a switch target the switch.
        foreach (var child in node.ChildNodes())
        {
            if (child is LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax
                or LocalFunctionStatementSyntax)
                continue;
            if (child is ReturnStatementSyntax or GotoStatementSyntax)
                return true;
            if (child is BreakStatementSyntax && breakExits && !inSwitch)
                return true;
            if (child is SwitchStatementSyntax)
            {
                if (LoopBodyCanExit(child, inSwitch: true, breakExits))
                    return true;
                continue;
            }
            if (child is WhileStatementSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or DoStatementSyntax)
            {
                if (LoopBodyCanExit(child, inSwitch: false, breakExits: false))
                    return true;
                continue;
            }
            if (LoopBodyCanExit(child, inSwitch, breakExits))
                return true;
        }

        return false;
    }


    private static bool IsDefinitelyNull(ExpressionSyntax expression, SemanticModel? model)
    {
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NullLiteralExpression))
            return true;
        // Peel only null-preserving conversions: a user-defined operator may
        // map null to a non-null instance. Parentheses never change the value.
        var current = expression;
        while (true)
        {
            while (current is ParenthesizedExpressionSyntax parenthesized)
                current = parenthesized.Expression;
            if (current is CastExpressionSyntax cast)
            {
                if (model?.GetOperation(cast) is IConversionOperation { OperatorMethod: not null })
                    return false;
                current = cast.Expression;
                continue;
            }

            break;
        }

        if (current is LiteralExpressionSyntax literalNull &&
            literalNull.IsKind(SyntaxKind.NullLiteralExpression))
            return true;
        if (model == null)
            return false;
        return model.GetOperation(current)?.ConstantValue is { HasValue: true, Value: null };
    }

    private static bool ExpressionAlwaysThrows(ExpressionSyntax expression, SemanticModel? model)
    {
        // An expression provably throws on every evaluation. Conditional
        // positions (ternary arms, short-circuit right operands, null-skipped
        // access) propagate only when taken; deferred bodies (lambdas, local
        // functions) never fire at the call site.
        var current = expression;
        while (true)
        {
            if (current is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized.Expression;
                continue;
            }

            if (current is CastExpressionSyntax cast)
            {
                current = cast.Expression;
                continue;
            }

            break;
        }

        return current switch
        {
            ThrowExpressionSyntax => true,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                IsDefinitelyNull(binary.Left, model) && ExpressionAlwaysThrows(binary.Right, model),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
                ConstantBool(binary.Left, model) == true && ExpressionAlwaysThrows(binary.Right, model),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                ConstantBool(binary.Left, model) == false && ExpressionAlwaysThrows(binary.Right, model),
            ConditionalExpressionSyntax conditional =>
                ExpressionAlwaysThrows(conditional.WhenTrue, model) &&
                ExpressionAlwaysThrows(conditional.WhenFalse, model),
            SwitchExpressionSyntax switchExpression =>
                switchExpression.Arms.Count > 0 &&
                switchExpression.Arms.All(arm => ExpressionAlwaysThrows(arm.Expression, model)),
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) =>
                IsDefinitelyNull(assignment.Left, model) && ExpressionAlwaysThrows(assignment.Right, model),
            AssignmentExpressionSyntax assignment =>
                ExpressionAlwaysThrows(assignment.Right, model),
            AwaitExpressionSyntax awaited =>
                ExpressionAlwaysThrows(awaited.Expression, model),
            InvocationExpressionSyntax invocation =>
                (invocation.Expression != null && ExpressionAlwaysThrows(invocation.Expression, model)) ||
                invocation.ArgumentList.Arguments.Any(argument => ExpressionAlwaysThrows(argument.Expression, model)),
            ElementAccessExpressionSyntax elementAccess =>
                ExpressionAlwaysThrows(elementAccess.Expression, model) ||
                elementAccess.ArgumentList.Arguments.Any(argument => ExpressionAlwaysThrows(argument.Expression, model)),
            MemberAccessExpressionSyntax memberAccess =>
                ExpressionAlwaysThrows(memberAccess.Expression, model),
            _ => false,
        };
    }

    private static bool StatementNeverCompletesNormally(StatementSyntax statement, SemanticModel? model)
    {
        // A bare nested block completes normally exactly when its last statement
        // does. An if/else with two terminating branches, an exitless
        // unconditional loop, or a try with a terminating finally or with an
        // always-throwing body whose throws all escape never completes.
        // Anything else either completes or returns to the caller (return), so
        // only these shapes block the save.
        if (statement is ThrowStatementSyntax)
            return true;

        // A return whose expression always throws never completes normally
        // either: it throws instead of returning to the caller.
        if (statement is ReturnStatementSyntax returnStatement &&
            returnStatement.Expression != null &&
            ExpressionAlwaysThrows(returnStatement.Expression, model))
            return true;

        // An expression statement that always throws (e.g. `_ = (string)null
        // ?? throw ...`) never completes normally either, as does a local
        // declaration whose only initializer does.
        if (statement is ExpressionStatementSyntax expressionStatement &&
            ExpressionAlwaysThrows(expressionStatement.Expression, model))
            return true;
        if (statement is LocalDeclarationStatementSyntax declaration &&
            declaration.Declaration.Variables.Count == 1 &&
            declaration.Declaration.Variables[0].Initializer?.Value is { } initializer &&
            ExpressionAlwaysThrows(initializer, model))
            return true;

        if (LoopNeverExits(statement))
            return true;

        if (statement is BlockSyntax nested && nested.Statements.Count > 0)
            return StatementNeverCompletesNormally(nested.Statements[nested.Statements.Count - 1], model);

        if (statement is IfStatementSyntax conditional
            && conditional.Else?.Statement is { } elseBranch)
            return StatementNeverCompletesNormally(conditional.Statement, model)
                && StatementNeverCompletesNormally(elseBranch, model);

        if (statement is TryStatementSyntax attempt)
        {
            if (attempt.Finally?.Block is { } cleanup
                && cleanup.Statements.Count > 0
                && StatementNeverCompletesNormally(cleanup.Statements[cleanup.Statements.Count - 1], model))
                return true;

            if (attempt.Block?.Statements.Count > 0
                && StatementNeverCompletesNormally(
                    attempt.Block.Statements[attempt.Block.Statements.Count - 1], model))
            {
                // The body always throws: the try never completes only when every
                // unconditional throw escapes its handlers (resolved in order).
                var throws = TryBodyThrows(attempt.Block);
                return throws.Count > 0
                    && throws.All(throwStatement => !ThrowResumes(throwStatement, attempt, model));
            }

            return false;
        }

        return false;
    }

    private static List<ThrowStatementSyntax> TryBodyThrows(BlockSyntax body)
    {
        var throws = new List<ThrowStatementSyntax>();
        foreach (var throwStatement in body.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            // Throws inside nested executables do not fire on try entry.
            if (throwStatement.Ancestors().Any(ancestor =>
                    ancestor is LambdaExpressionSyntax
                        or AnonymousMethodExpressionSyntax
                        or LocalFunctionStatementSyntax
                    && body.Span.Contains(ancestor.Span)))
                continue;

            throws.Add(throwStatement);
        }

        return throws;
    }

    private static bool IsDirectCalleeOperation(
        SyntaxNode node,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // Nested local functions and lambdas are separate executables: their
        // tracking effects only matter if the nested executable itself runs.
        var boundary = node.Ancestors().FirstOrDefault(ancestor =>
            ancestor is LocalFunctionStatementSyntax
                or LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
        return ReferenceEquals(boundary, localFunctionSyntax);
    }

    private static bool CalleeReattachPersistsMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax,
        int persistThroughSpan = int.MaxValue)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;

        // Blocks enclosing the mutation, innermost first: a reattachment dominates
        // the mutation when it sits in one of these blocks before the mutation's
        // path, even if the mutation itself is nested deeper.
        var mutationBlocks = new HashSet<SyntaxNode>(
            entry.Operation.Syntax.Ancestors().OfType<BlockSyntax>());
        var mutationSpan = entry.Operation.Syntax.SpanStart;
        for (var i = 0; i < reattaches.Count; i++)
        {
            var reattach = reattaches[i];
            if (!localFunctionSyntax.Span.Contains(reattach.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(reattach.Operation.Syntax, localFunctionSyntax))
                continue;
            if (reattach.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(reattach.ContextSymbol, saveContext))
                continue;
            if (!ReattachCoversPath(reattach, entry.ReceiverPath))
                continue;

            var reattachBlock = reattach.Operation.Syntax.Ancestors()
                .OfType<BlockSyntax>()
                .FirstOrDefault();
            var dominates = reattachBlock != null && mutationBlocks.Contains(reattachBlock);
            // Block-sharing alone proves nothing when the reattachment sits
            // under optional control flow (e.g. an unbraced conditional arm):
            // it must run on every path through the block.
            if (dominates && reattachBlock != null &&
                reattach.Operation.SemanticModel?.GetOperation(reattachBlock) is IBlockOperation reattachBlockOp &&
                IsNestedUnderOptionalControlFlow(reattach.Operation, reattachBlockOp, entry.Operation))
                dominates = false;
            if (!dominates)
            {
                // A finally block always executes when its try statement is entered:
                // an update there persists the mutation, and an attach there
                // precedes any mutation after the try statement (span ordering
                // below keeps attach-after-mutation quiet-safe).
                var finallyClause = reattach.Operation.Syntax.Ancestors()
                    .OfType<FinallyClauseSyntax>()
                    .FirstOrDefault();
                dominates = finallyClause != null
                    && finallyClause.Parent is TryStatementSyntax tryStatement
                    && tryStatement.Span.Contains(mutationSpan)
                    && localFunctionSyntax.Span.Contains(tryStatement.Span);
            }

            if (!dominates)
            {
                // A reattachment in a preceding sibling bare block (e.g. `{ Attach; }`
                // before the mutation) executes unconditionally on every path that
                // reaches the mutation, provided the reattach itself is not nested
                // in a conditional inside that block and nothing untracks between.
                dominates = PrecedingBlockDominates(
                    reattach, mutationSpan, mutationBlocks, localFunctionSyntax);
            }

            if (!dominates)
            {
                // A reattachment in a try block whose handlers all terminate (or
                // which has no handlers) is guaranteed on every path that reaches
                // past the statement: success attaches, failure diverts to a
                // terminating handler and never reaches the mutation.
                dominates = TryBlockDominates(
                    scan, local, saveContext, entry, reattach, mutationSpan, mutationBlocks, entry.Operation.SemanticModel, localFunctionSyntax);
            }

            if (!dominates && reattach.PersistsExistingMutation)
            {
                // A persisting reattachment (Update, Modified state) in a following
                // sibling bare block still runs after the mutation on every path
                // that reaches it, unless control diverts between the two.
                dominates = FollowingBlockDominates(
                    scan, local, saveContext, entry, reattach, mutationSpan,
                    mutationBlocks, localFunctionSyntax);
            }

            if (!dominates)
            {
                // Exhaustive branches that both reattach (e.g. attach in the then
                // and else arms) track the entity on every path past the branch.
                dominates = CollectiveBranchReattach(
                    scan, local, saveContext, entry, reattach, mutationSpan,
                    mutationBlocks, localFunctionSyntax);
            }

            if (!dominates)
                continue;

            if (CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
                    reattach.Operation.Syntax.SpanStart, mutationSpan, mutationSpan, entry.Operation.Syntax, entry.Operation.SemanticModel, localFunctionSyntax))
                continue;

            // A detach or clear after the reattachment removes tracking again:
            // the mutation is lost despite the earlier persistence.
            if (CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
                    reattach.Operation.Syntax.SpanStart, persistThroughSpan, mutationSpan, entry.Operation.Syntax, entry.Operation.SemanticModel, localFunctionSyntax))
                continue;
            // A transfer between the mutation and a later reattachment in the
            // same block can skip the update while still reaching the save.
            // (For earlier reattachments the window below is empty.)
            if (reattachBlock != null &&
                BetweenDiverts(
                    mutationSpan, reattach.Operation.Syntax.SpanStart, reattachBlock,
                    entry.Operation.SemanticModel, localFunctionSyntax,
                    scan, local, saveContext, entry.ReceiverPath))
                continue;
            if (reattach.PersistsExistingMutation)
                return true;

            // Attach only helps before the mutation; attaching after a mutation on an
            // untracked entity does not persist it.
            if (reattach.Operation.Syntax.SpanStart < mutationSpan)
                return true;
        }

        return false;
    }

    private static bool NestedHelperReattaches(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax,
        Compilation compilation)
    {
        // A nested local function that reattaches the same entity and is invoked
        // unconditionally before the mutation tracks it just like a direct
        // attach. Sibling helpers in the enclosing scope count too: invocation
        // is resolved by symbol, so visibility is enforced by the compiler.
        // Only straight-line invocations count; anything conditional keeps
        // the lift.
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;

        var mutationSpan = entry.Operation.Syntax.SpanStart;
        var model = compilation.GetSemanticModel(localFunctionSyntax.SyntaxTree);
        var scope = localFunctionSyntax.Ancestors().OfType<BlockSyntax>().LastOrDefault();
        var helpers = scope != null
            ? scope.DescendantNodes().OfType<LocalFunctionStatementSyntax>()
            : localFunctionSyntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>();
        foreach (var nested in helpers)
        {
            if (nested == localFunctionSyntax)
                continue;
            if (model.GetDeclaredSymbol(nested) is not IMethodSymbol nestedSymbol)
                continue;
            // Calling an iterator never executes its body; enumeration does.
            // (An enumerated iterator that attaches is a residual gap.)
            if (nested.DescendantNodes().OfType<YieldStatementSyntax>().Any())
                continue;

            var hasAttach = false;
            var hasPersisting = false;
            foreach (var candidate in reattaches)
            {
                if (!nested.Span.Contains(candidate.Operation.Syntax.Span))
                    continue;
                if (candidate.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(candidate.ContextSymbol, saveContext))
                    continue;
                if (!ReattachCoversPath(candidate, entry.ReceiverPath))
                    continue;

                var boundary = candidate.Operation.Syntax.Ancestors()
                    .FirstOrDefault(ancestor => ancestor is LocalFunctionStatementSyntax
                        or LambdaExpressionSyntax
                        or AnonymousMethodExpressionSyntax);
                if (!ReferenceEquals(boundary, nested))
                    continue;
                // The reattachment itself must run on every helper path, not
                // just sit in the body: a conditional attach leaves paths
                // untracked.
                if (nested.Body != null && !IsUnconditionalWithin(candidate.Operation.Syntax, nested.Body))
                    continue;

                // A helper that untracks after reattaching leaves the entity
                // untracked: only reattachments without a later invalidation
                // inside the helper count.
                if (NestedInvalidatedAfter(
                        scan, local, saveContext, entry,
                        candidate.Operation.Syntax.SpanStart, nested, model, localFunctionSyntax))
                    continue;

                if (candidate.PersistsExistingMutation)
                    hasPersisting = true;
                else
                    hasAttach = true;
            }


            if (!hasAttach && !hasPersisting)
                continue;

            foreach (var invocation in localFunctionSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (nested.Span.Contains(invocation.Span))
                    continue;
                // Attach must precede the mutation; a persisting reattachment
                // counts wherever the helper runs: nested timing follows the
                // outer invocation, which the caller already windows.
                if (invocation.SpanStart >= mutationSpan && !hasPersisting)
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(invocation).Symbol?.OriginalDefinition,
                        nestedSymbol.OriginalDefinition))
                    continue;

                // Straight-line position within the callee body and no dead
                // guard: the helper provably runs before the mutation.
                if (localFunctionSyntax.Body == null
                    || !IsUnconditionalWithin(invocation, localFunctionSyntax.Body))
                    continue;
                if (DeadGuardEncloses(invocation, invocation.SpanStart, model, localFunctionSyntax))
                    continue;

                // Tracking must survive from the helper invocation through the
                // mutation: a detach or clear later in the callee still loses it.
                if (CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
                        invocation.SpanStart, int.MaxValue, mutationSpan, entry.Operation.Syntax, model, localFunctionSyntax))
                    continue;
                return true;
            }
        }

        return false;
    }
    private static bool CalleeCatchPersistsMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // When the try block cannot complete normally after the mutation, every
        // mutation path transfers into a handler: if the applicable handlers
        // all reattach the same entity, the mutation persists on every path.
        var mutationSyntax = entry.Operation.Syntax;
        var mutationSpan = mutationSyntax.SpanStart;
        var model = entry.Operation.SemanticModel;
        var tryStatement = mutationSyntax.Ancestors().OfType<TryStatementSyntax>().FirstOrDefault();
        if (tryStatement == null ||
            !localFunctionSyntax.Span.Contains(tryStatement.Span) ||
            tryStatement.Block != mutationSyntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault())
            return false;

        var passedPath = false;
        var allTransfer = false;
        foreach (var statement in tryStatement.Block.Statements)
        {
            if (!passedPath)
            {
                if (statement.Span.Contains(mutationSpan))
                    passedPath = true;
                continue;
            }

            // Completing statements pass control onward; a return or goto
            // bypasses every handler with an unpersisted mutation. Only an
            // unconditional terminator proves all paths transfer.
            if (statement is ReturnStatementSyntax or GotoStatementSyntax)
                return false;
            if (StatementNeverCompletesNormally(statement, model))
            {
                allTransfer = true;
                break;
            }
        }

        if (!passedPath || !allTransfer)
            return false;

        var throws = TryBodyThrows(tryStatement.Block)
            .Where(throwStatement => throwStatement.SpanStart > mutationSpan)
            .ToList();
        if (throws.Count == 0)
            return false;

        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;

        foreach (var throwStatement in throws)
        {
            if (!ThrowResumesInUpdatingHandler(
                    throwStatement, tryStatement, scan, local, saveContext, entry, model, localFunctionSyntax))
                return false;
        }

        return true;
    }

    private static bool ThrowResumesInUpdatingHandler(
        ThrowStatementSyntax throwStatement,
        TryStatementSyntax tryStatement,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        var thrown = throwStatement.Expression != null
            ? model?.GetTypeInfo(throwStatement.Expression).Type
            : null;
        foreach (var catchClause in tryStatement.Catches)
        {
            if (catchClause.Filter != null
                && ConstantBool(catchClause.Filter.FilterExpression, model) is not true)
                continue;
            if (!CatchMatches(catchClause.Declaration, thrown, model))
                continue;

            // First matching handler decides: it must persist the existing
            // change, since attaching after the mutation does not save it.
            if (catchClause.Block.Statements.Count == 0)
                return false;

            return CatchBlockReattaches(
                scan, local, saveContext, entry, catchClause.Block, localFunctionSyntax, requirePersisting: true);
        }

        return false;
    }



    private static bool CollectiveBranchReattach(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        ReattachEntry reattach,
        int mutationSpan,
        HashSet<SyntaxNode> mutationBlocks,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // When both arms of the same if/else reattach, the entity is tracked on
        // every path past the branch. The branch itself takes the dominance
        // position of a straight-line reattach.
        var reattachSyntax = reattach.Operation.Syntax;
        var ifStatement = reattachSyntax.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
        if (ifStatement == null ||
            ifStatement.Else?.Statement == null ||
            !localFunctionSyntax.Span.Contains(ifStatement.Span))
            return false;

        var reattachArm = BranchArm(ifStatement, reattachSyntax.SpanStart);
        if (reattachArm == 0 || !IsUnconditionalWithin(reattachSyntax, ArmStatement(ifStatement, reattachArm)))
            return false;

        var otherArm = reattachArm == 1 ? ifStatement.Else.Statement : ifStatement.Statement;
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;

        foreach (var partner in reattaches)
        {
            if (ReferenceEquals(partner.Operation, reattach.Operation))
                continue;
            if (!otherArm.Span.Contains(partner.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(partner.Operation.Syntax, localFunctionSyntax))
                continue;
            if (partner.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(partner.ContextSymbol, saveContext))
                continue;
            if (!ReattachCoversPath(partner, entry.ReceiverPath))
                continue;
            if (!IsUnconditionalWithin(partner.Operation.Syntax, otherArm))
                continue;

            // Both arms reattach: the branch dominates exactly where a
            // straight-line reattach at the branch position would.
            var branchBlock = ifStatement.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (branchBlock == null || !mutationBlocks.Contains(branchBlock))
                return false;

            var virtualSpan = ifStatement.SpanStart;
            if (CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
                    virtualSpan, mutationSpan, mutationSpan, entry.Operation.Syntax, entry.Operation.SemanticModel, localFunctionSyntax))
                return false;

            // Before the mutation either arm tracks the entity, so any pair
            // suffices. After it, an attach-only arm loses the write: both
            // arms must persist the existing mutation.
            if (virtualSpan < mutationSpan)
                return true;

            return reattach.PersistsExistingMutation && partner.PersistsExistingMutation;
        }

        var terminatingArm = reattachArm == 1 ? ifStatement.Else?.Statement : ifStatement.Statement;
        var fallbackSpan = ifStatement.SpanStart;
        var armTerminates = terminatingArm != null
            && (StatementNeverCompletesNormally(terminatingArm, entry.Operation.SemanticModel)
                || (ArmReturns(terminatingArm) && fallbackSpan < mutationSpan));
        if (armTerminates)
        {
            var branchBlock = ifStatement.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (branchBlock == null || !mutationBlocks.Contains(branchBlock))
                return false;

            if (CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
                    fallbackSpan, mutationSpan, mutationSpan, entry.Operation.Syntax, entry.Operation.SemanticModel, localFunctionSyntax))
                return false;

            if (reattach.PersistsExistingMutation)
                return true;

            return fallbackSpan < mutationSpan;
        }

        return false;
    }

    private static bool ArmReturns(StatementSyntax arm)
    {
        // A bare return ends the path even through nested bare blocks. Other
        // jumps (goto into the mutation, break/continue) do not prove the
        // mutation unreachable, so they never count here.
        var current = arm;
        while (current is BlockSyntax block && block.Statements.Count > 0)
            current = block.Statements[block.Statements.Count - 1];
        return current is ReturnStatementSyntax;
    }

    private static bool CatchIsImpossible(CatchClauseSyntax catchClause, SemanticModel? model)
    {
        // A handler with a constant-false filter never executes: it blocks
        // neither tracking nor termination.
        return catchClause.Filter != null
            && ConstantBool(catchClause.Filter.FilterExpression, model) == false;
    }

    private static bool TryBlockDominates(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        ReattachEntry reattach,
        int mutationSpan,
        HashSet<SyntaxNode> mutationBlocks,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // The reattachment must sit directly in a try block (bare block or
        // expression statement only between) whose try statement precedes the
        // mutation, with every handler terminating (return/throw) or absent:
        // paths reaching the mutation all attached successfully.
        var reattachSyntax = reattach.Operation.Syntax;
        var tryBlock = reattachSyntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (tryBlock == null ||
            tryBlock.Span.End > mutationSpan ||
            !localFunctionSyntax.Span.Contains(tryBlock.Span))
            return false;

        foreach (var ancestor in reattachSyntax.Ancestors())
        {
            if (ancestor == (SyntaxNode)tryBlock)
                break;
            if (ancestor is not ExpressionStatementSyntax)
                return false;
        }

        TryStatementSyntax? tryStatement = tryBlock.Parent as TryStatementSyntax;
        var fromCatch = false;
        if (tryStatement == null)
        {
            // A preceding finally block always executes when its try statement
            // runs, so attachment there precedes the mutation unconditionally.
            if (tryBlock.Parent is FinallyClauseSyntax finallyClause
                && finallyClause.Parent is TryStatementSyntax enclosingTry)
                tryStatement = enclosingTry;
            // A preceding catch block attaches on every path past the
            // statement only when the try cannot complete normally: every
            // path transfers into some handler.
            else if (tryBlock.Parent is CatchClauseSyntax onlyCatch
                && onlyCatch.Parent is TryStatementSyntax catchingTry)
            {
                tryStatement = catchingTry;
                fromCatch = true;
            }
        }

        if (tryStatement == null ||
            !localFunctionSyntax.Span.Contains(tryStatement.Span) ||
            tryStatement.Span.End > mutationSpan)
            return false;

        var current = (SyntaxNode)tryStatement;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            if (parent is BlockSyntax parentBlock)
            {
                if (mutationBlocks.Contains(parentBlock))
                    break;
                current = parent;
                continue;
            }

            return false;
        }

        // A reattachment in the finally block needs no handler analysis: the
        // finally runs before the mutation on every path past the statement,
        // whether the try succeeded or a handler completed.
        if (tryBlock.Parent is FinallyClauseSyntax)
            return true;

        // A reattachment in a catch block dominates a later mutation only when
        // the try cannot complete normally and every throw resolves in order
        // to a handler that attaches: reaching past the statement then implies
        // attachment on every path.
        if (fromCatch)
        {
            var body = tryStatement.Block;
            if (body?.Statements.Count is not > 0
                || !PreMutationTerminates(body.Statements[body.Statements.Count - 1], model))
                return false;

            foreach (var throwStatement in TryBodyThrows(tryStatement.Block))
                if (!ThrowResolvesToAttaching(throwStatement, tryStatement, scan, local, saveContext, entry, model, localFunctionSyntax))
                    return false;

            return true;
        }
        foreach (var catchClause in tryStatement.Catches)
        {
            // An impossible handler (`when (false)`) never executes: it blocks
            // neither tracking nor termination. Otherwise only terminating
            // handlers dominate: an empty or completing handler falls through
            // to the mutation without tracking. Return exits the callee, so it
            // terminates the path just like a throw.
            if (CatchIsImpossible(catchClause, model))
                continue;
            if (catchClause.Block.Statements.Count == 0)
                return false;

            var last = catchClause.Block.Statements[catchClause.Block.Statements.Count - 1];
            if (last is not ReturnStatementSyntax
                && !StatementNeverCompletesNormally(last, model)
                && !CatchBlockReattaches(scan, local, saveContext, entry, catchClause.Block, localFunctionSyntax))
                return false;
        }
        return true;
    }

    private static bool ThrowResolvesToAttaching(
        ThrowStatementSyntax throwStatement,
        TryStatementSyntax tryStatement,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        var thrown = throwStatement.Expression != null
            ? model?.GetTypeInfo(throwStatement.Expression).Type
            : null;
        foreach (var catchClause in tryStatement.Catches)
        {
            if (CatchIsImpossible(catchClause, model))
                continue;
            if (catchClause.Filter != null
                && ConstantBool(catchClause.Filter.FilterExpression, model) is not true)
                continue;
            if (!CatchMatches(catchClause.Declaration, thrown, model))
                continue;

            // First matching handler decides: it must attach unconditionally.
            // (Attach-before suffices here since the handler precedes the
            // mutation, unlike the post-mutation catch path.)
            return CatchBlockReattaches(
                scan, local, saveContext, entry, catchClause.Block, localFunctionSyntax);
        }

        return false;
    }



    private static bool CatchBlockReattaches(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        BlockSyntax catchBlock,
        LocalFunctionStatementSyntax localFunctionSyntax,
        bool requirePersisting = false)
    {
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;

        foreach (var candidate in reattaches)
        {
            if (!catchBlock.Span.Contains(candidate.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(candidate.Operation.Syntax, localFunctionSyntax))
                continue;
            if (candidate.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContextSymbol, saveContext))
                continue;
            if (!ReattachCoversPath(candidate, entry.ReceiverPath))
                continue;
            // Attaching after the mutation snapshots the already-modified value
            // without persisting it: only tracking that persists an existing
            // change counts on the catch path.
            if (requirePersisting && !candidate.PersistsExistingMutation)
                continue;
            if (!IsUnconditionalWithin(candidate.Operation.Syntax, catchBlock))
                continue;

            return true;
        }

        return false;
    }

    private static bool UnreachableGuardEncloses(
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // Constant-folded guards (`if (false)`, `while (false)`) make the
        // mutation dead code: declaration position must not trigger a lift.
        return DeadGuardEncloses(
            entry.Operation.Syntax,
            entry.Operation.Syntax.SpanStart,
            entry.Operation.SemanticModel,
            localFunctionSyntax);
    }

    private static bool DeadGuardEncloses(
        SyntaxNode node,
        int position,
        SemanticModel? model,
        SyntaxNode boundary)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (!boundary.Span.Contains(ancestor.Span))
                break;

            if (ancestor is IfStatementSyntax ifStatement
                && ConstantBool(ifStatement.Condition, model) is { } branch)
            {
                var inThen = ifStatement.Statement?.Span.Contains(position) == true;
                var inElse = ifStatement.Else?.Span.Contains(position) == true;
                if ((!branch && inThen) || (branch && inElse && ifStatement.Else != null))
                    return true;
            }

            if (ancestor is ConditionalExpressionSyntax ternary
                && ConstantBool(ternary.Condition, model) is { } pick)
            {
                var inTrue = ternary.WhenTrue.Span.Contains(position);
                var inFalse = ternary.WhenFalse.Span.Contains(position);
                if ((!pick && inTrue) || (pick && inFalse))
                    return true;
            }

            if (ancestor is WhileStatementSyntax whileLoop
                && ConstantBool(whileLoop.Condition, model) == false
                && whileLoop.Statement.Span.Contains(position))
                return true;

            // The body of `for (; false;)` never executes (the initializer and
            // condition still run, but the code inside the body is dead).
            if (ancestor is ForStatementSyntax forLoop
                && ConstantBool(forLoop.Condition, model) == false
                && forLoop.Statement.Span.Contains(position))
                return true;

            // The right operand of `false && X` or `true || X` never evaluates.
            if (ancestor is BinaryExpressionSyntax binary
                && binary.Right.Span.Contains(position)
                && ConstantBool(binary.Left, model) is { } left)
            {
                if (binary.IsKind(SyntaxKind.LogicalAndExpression) && !left)
                    return true;
                if (binary.IsKind(SyntaxKind.LogicalOrExpression) && left)
                    return true;
            }

            // The right operand of `X ?? Y` never evaluates when X is provably
            // non-null: the coalescing short-circuits before it.
            if (ancestor is BinaryExpressionSyntax coalesce
                && coalesce.IsKind(SyntaxKind.CoalesceExpression)
                && coalesce.Right.Span.Contains(position)
                && ConstantValue(coalesce.Left, model) is { HasValue: true, Value: not null })
                return true;

            // An argument of a `?.` call with a provably null receiver never
            // evaluates: the whole access short-circuits to null.
            if (ancestor is ArgumentSyntax
                && ancestor.Parent is ArgumentListSyntax argumentList
                && argumentList.Parent is InvocationExpressionSyntax invoked
                && invoked.Parent is ConditionalAccessExpressionSyntax access
                && access.Expression is { } receiver
                && ConstantValue(receiver, model) is { HasValue: true, Value: null })
                return true;
        }

        return false;
    }

    private static bool? ConstantBool(ExpressionSyntax? condition, SemanticModel? model)
    {
        if (ConstantValue(condition, model) is { HasValue: true, Value: bool value })
            return value;

        return null;
    }

    private static Optional<object?> ConstantValue(ExpressionSyntax? expression, SemanticModel? model)
    {
        // Casts and parentheses block Roslyn's constant folding (notably
        // `(bool?)true`), so peel them before asking for the value. The
        // runtime value is unchanged by the peel.
        while (expression is CastExpressionSyntax cast)
            expression = cast.Expression;
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        if (expression == null || model == null)
            return default;

        return model.GetOperation(expression)?.ConstantValue ?? default;
    }


    private static StatementSyntax? ArmStatement(IfStatementSyntax ifStatement, int arm)
    {
        return arm == 1 ? ifStatement.Statement : ifStatement.Else?.Statement;
    }

    private static bool IsUnconditionalWithin(SyntaxNode node, SyntaxNode? boundary)
    {
        // Every enclosing statement up to the arm must be a bare block or the
        // expression statement itself: no nested branch, loop, try, or lambda.
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor == boundary)
                return true;
            if (ancestor is not BlockSyntax and not ExpressionStatementSyntax)
                return false;
        }

        return false;
    }

    private static bool PrecedingBlockDominates(
        ReattachEntry reattach,
        int mutationSpan,
        HashSet<SyntaxNode> mutationBlocks,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // The reattachment's block must reach one of the mutation's ancestor
        // blocks through bare blocks only, ending before the mutation starts,
        // with the reattach unconditional inside its own block. Any conditional,
        // loop, or try between means tracking is not guaranteed.
        var reattachSyntax = reattach.Operation.Syntax;
        var innerBlock = reattachSyntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (innerBlock == null ||
            innerBlock.Span.End > mutationSpan ||
            !localFunctionSyntax.Span.Contains(innerBlock.Span))
            return false;

        foreach (var ancestor in reattachSyntax.Ancestors())
        {
            if (ancestor == (SyntaxNode)innerBlock)
                break;

            // The invocation itself sits in an expression statement; any other
            // enclosing statement (branch, loop, try, lambda) makes tracking
            // conditional. A using statement (braced or unbraced) always runs
            // its embedded statement when reached.
            if (ancestor is not ExpressionStatementSyntax and not UsingStatementSyntax)
                return false;
        }

        var current = (SyntaxNode)innerBlock;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            if (parent is BlockSyntax parentBlock)
            {
                if (mutationBlocks.Contains(parentBlock))
                    return true;
                current = parent;
                continue;
            }

            // A do-while body always executes once, and lock and using bodies
            // execute exactly once when reached, so all three are transparent
            // on the path. While/for bodies may never run. (A lock that throws
            // on entry or a disposal that throws still keeps the save
            // unreachable on those paths.)
            if (parent is DoStatementSyntax or LockStatementSyntax or UsingStatementSyntax)
            {
                current = parent;
                continue;
            }

            return false;
        }

        return false;
    }
    private static bool FollowingBlockDominates(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        ReattachEntry reattach,
        int mutationSpan,
        HashSet<SyntaxNode> mutationBlocks,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // Mirror of the preceding rule: a persisting reattachment in a following
        // sibling bare block runs after the mutation on every path that reaches
        // it. Control diverting between the two (return, goto, loop exits) or an
        // untracking operation means the update may be skipped: stay lifted.
        var reattachSyntax = reattach.Operation.Syntax;
        var innerBlock = reattachSyntax.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (innerBlock == null ||
            innerBlock.SpanStart < mutationSpan ||
            !localFunctionSyntax.Span.Contains(innerBlock.Span))
            return false;

        foreach (var ancestor in reattachSyntax.Ancestors())
        {
            if (ancestor == (SyntaxNode)innerBlock)
                break;
            if (ancestor is not ExpressionStatementSyntax and not UsingStatementSyntax)
                return false;
        }

        var current = (SyntaxNode)innerBlock;
        BlockSyntax? shared = null;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            if (parent is BlockSyntax parentBlock)
            {
                if (mutationBlocks.Contains(parentBlock))
                {
                    shared = parentBlock;
                    break;
                }

                current = parent;
                continue;
            }

            // A try statement with terminating (or absent) handlers is
            // transparent: success runs the update, failure never reaches the
            // mutation. A do-while body always executes once, and lock and
            // using bodies execute exactly once when reached, so they are
            // transparent as well. Anything else conditional blocks the guarantee.
            if (parent is TryStatementSyntax followingTry
                && localFunctionSyntax.Span.Contains(followingTry.Span)
                && followingTry.SpanStart > mutationSpan
                && TryHandlersTerminate(followingTry, entry.Operation.SemanticModel))
            {
                current = parent;
                continue;
            }

            if (parent is DoStatementSyntax or LockStatementSyntax or UsingStatementSyntax)
            {
                current = parent;
                continue;
            }

            return false;
        }

        // Scan through the reattachment itself: a transfer inside the following
        // block (including a using body) before the update skips it while the
        // save still runs. Transfers after the update ran cannot void it.
        if (shared == null || BetweenDiverts(mutationSpan, reattachSyntax.SpanStart, shared, entry.Operation.SemanticModel, localFunctionSyntax, scan, local, saveContext, entry.ReceiverPath))
            return false;

        // Only invalidations after the update matter: anything between the
        // mutation and the update is superseded by the update reattaching the
        // entity, while a later detach or clear removes tracking again.
        return !CalleeRangeUntracks(scan, local, saveContext, entry.ReceiverPath,
            reattachSyntax.SpanStart, int.MaxValue, mutationSpan, entry.Operation.Syntax, entry.Operation.SemanticModel, localFunctionSyntax);
    }
    private static bool BetweenDiverts(
        int startSpan,
        int endSpan,
        BlockSyntax shared,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath)
    {
        // A return, goto, break, or continue between the mutation and the
        // following block can skip the update while still reaching the save.
        foreach (var jump in shared.DescendantNodes().OfType<StatementSyntax>())
        {
            if (jump is not BreakStatementSyntax
                and not ContinueStatementSyntax
                and not GotoStatementSyntax
                and not ReturnStatementSyntax)
                continue;

            if (jump.SpanStart <= startSpan || jump.SpanStart >= endSpan)
                continue;
            // A transfer in a branch mutually exclusive with the mutation can
            // never execute on the mutation's path.
            if (BranchesAreMutuallyExclusive(jump, startSpan, localFunctionSyntax))
                continue;
            // A return nullified by a terminating finally never reaches past
            // it: the throwing path cannot arrive at the save.
            if (jump is ReturnStatementSyntax returnStatement &&
                ReturnSuppressedByFinally(returnStatement, model, localFunctionSyntax))
                continue;
            // Unreachable transfers (constant-dead guards) never divert.
            if (DeadGuardEncloses(jump, jump.SpanStart, model, localFunctionSyntax))
                continue;

            var boundary = jump.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (!ReferenceEquals(boundary, localFunctionSyntax))
                continue;

            // A break or continue that stays within an inner construct still
            // executes the update: only a transfer out past the reattachment
            // bypasses it. A goto landing at or before the reattachment still
            // reaches it. Returns, throws, and unresolvable jumps are handled
            // by their own reachability rules above.
            if ((jump is BreakStatementSyntax || jump is ContinueStatementSyntax) &&
                !TransferSkipsPosition(jump, endSpan))
                continue;
            if (jump is GotoStatementSyntax gotoStatement &&
                !GotoSkipsPosition(
                    gotoStatement, endSpan, startSpan, model, localFunctionSyntax,
                    scan, local, saveContext, receiverPath))
                continue;
            // A diverted path that already reattached the mutation persists it:
            // the transfer skips only the later reattachment, not the write.
            if (DivertedPathAlreadyPersists(
                    scan, local, saveContext, receiverPath, jump,
                    model, localFunctionSyntax))
                continue;
            // A transfer out of a try whose finally reattaches still persists
            // the mutation while unwinding.
            if (JumpRunsFinallyReattach(
                    jump, scan, local, saveContext, receiverPath,
                    model, localFunctionSyntax))
                continue;
            return true;
        }
        return false;
    }

    private static bool DivertedPathAlreadyPersists(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        SyntaxNode jump,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A persisting reattachment that dominates the transfer persists on
        // that path whatever ran before: attaching marks the entity, so later
        // mutations stay tracked through the save.
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;
        var jumpOperation = model?.GetOperation(jump);
        foreach (var candidate in reattaches)
        {
            if (!candidate.PersistsExistingMutation)
                continue;
            if (candidate.SpanStart >= jump.SpanStart)
                continue;
            if (!localFunctionSyntax.Span.Contains(candidate.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(candidate.Operation.Syntax, localFunctionSyntax))
                continue;
            if (candidate.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContextSymbol, saveContext))
                continue;
            if (!ReattachCoversPath(candidate, receiverPath))
                continue;
            if (jumpOperation == null || !Dominates(candidate.Operation, jumpOperation))
                continue;
            // No invalidation may sit between the persisting reattachment and
            // the transfer.
            if (CalleeInvalidatedBetween(
                    scan, local, saveContext, receiverPath,
                    candidate.SpanStart, jump.SpanStart, model, localFunctionSyntax))
                continue;
            return true;
        }

        return false;
    }

    private static bool CalleeInvalidatedBetween(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        int fromSpan,
        int toSpan,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            foreach (var detach in detaches)
            {
                var syntax = detach.Operation.Syntax;
                if (detach.SpanStart <= fromSpan || detach.SpanStart >= toSpan)
                    continue;
                if (!localFunctionSyntax.Span.Contains(syntax.Span))
                    continue;
                if (!IsDirectCalleeOperation(syntax, localFunctionSyntax))
                    continue;
                if (detach.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(detach.ContextSymbol, saveContext))
                    continue;
                if (detach.TargetPath.Length != receiverPath.Length ||
                    !MemberPathIsPrefix(detach.TargetPath, receiverPath))
                    continue;
                if (DeadGuardEncloses(syntax, detach.SpanStart, model, localFunctionSyntax))
                    continue;
                return true;
            }
        }

        foreach (var clear in scan.TrackerClears)
        {
            var syntax = clear.Operation.Syntax;
            if (clear.SpanStart <= fromSpan || clear.SpanStart >= toSpan)
                continue;
            if (!localFunctionSyntax.Span.Contains(syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(syntax, localFunctionSyntax))
                continue;
            if (clear.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(clear.ContextSymbol, saveContext))
                continue;
            if (DeadGuardEncloses(syntax, clear.SpanStart, model, localFunctionSyntax))
                continue;
            return true;
        }

        return false;
    }

    private static bool TransferSkipsPosition(SyntaxNode jump, int position)
    {
        SyntaxNode? target = jump switch
        {
            BreakStatementSyntax =>
                jump.Ancestors().FirstOrDefault(ancestor =>
                    ancestor is WhileStatementSyntax or
                        DoStatementSyntax or
                        ForStatementSyntax or
                        ForEachStatementSyntax or
                        ForEachVariableStatementSyntax or
                        SwitchStatementSyntax),
            ContinueStatementSyntax =>
                jump.Ancestors().FirstOrDefault(ancestor =>
                    ancestor is WhileStatementSyntax or
                        DoStatementSyntax or
                        ForStatementSyntax or
                        ForEachStatementSyntax or
                        ForEachVariableStatementSyntax),
            _ => null,
        };
        return target?.Span.Contains(position) == true;
    }
    private static bool GotoSkipsPosition(
        GotoStatementSyntax gotoStatement,
        int position,
        int mutationSpan,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath)
    {
        // Only a forward jump over the reattachment skips it: backward jumps
        // re-run it, and jumps landing on or before it still reach it.
        if (gotoStatement.Expression is not IdentifierNameSyntax identifier)
            return true;
        SyntaxNode? target = null;
        foreach (var label in localFunctionSyntax.DescendantNodes().OfType<LabeledStatementSyntax>())
        {
            if (label.Identifier.ValueText != identifier.Identifier.ValueText)
                continue;
            var boundary = label.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (!ReferenceEquals(boundary, localFunctionSyntax))
                continue;
            if (target != null)
                return true;
            target = label;
        }

        if (target == null)
            return true;
        if (gotoStatement.SpanStart < position && position < target.SpanStart)
            return true;
        // A backward jump re-runs the reattachment unless the mutation sits
        // inside the loop and an exit between the label and the jump leaves
        // it while still reaching the save.
        return target.SpanStart < mutationSpan &&
            mutationSpan < gotoStatement.SpanStart &&
            LoopRegionHasExit(
                target, gotoStatement, position, mutationSpan, model, localFunctionSyntax,
                scan, local, saveContext, receiverPath);
    }
    private static bool LoopRegionHasExit(
        SyntaxNode label,
        GotoStatementSyntax gotoStatement,
        int position,
        int mutationSpan,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath)
    {
        // Any transfer that can leave the goto loop without running the
        // reattachment voids it: returns and escaping gotos always can, while
        // break/continue must demonstrably skip past it. Nested backward
        // gotos are conservatively exits (goto mazes do not get precision).
        foreach (var exit in localFunctionSyntax.DescendantNodes().OfType<StatementSyntax>())
        {
            if (exit.SpanStart <= label.SpanStart || exit.SpanStart >= gotoStatement.SpanStart)
                continue;
            if (exit is not BreakStatementSyntax
                and not ContinueStatementSyntax
                and not GotoStatementSyntax
                and not ReturnStatementSyntax)
                continue;
            if (DeadGuardEncloses(exit, exit.SpanStart, model, localFunctionSyntax))
                continue;
            var boundary = exit.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (!ReferenceEquals(boundary, localFunctionSyntax))
                continue;
            if (BranchesAreMutuallyExclusive(exit, mutationSpan, localFunctionSyntax))
                continue;
            if ((exit is BreakStatementSyntax || exit is ContinueStatementSyntax) &&
                !TransferSkipsPosition(exit, position))
                continue;
            if (exit is GotoStatementSyntax nestedGoto &&
                !NestedGotoSkipsPosition(nestedGoto, position, localFunctionSyntax))
                continue;
            // An exit whose own path already reattached persists: it leaves
            // the loop with the write intact.
            if (DivertedPathAlreadyPersists(
                    scan, local, saveContext, receiverPath, exit,
                    model, localFunctionSyntax))
                continue;
            // An exit unwinding through a finally that reattaches persists
            // while unwinding.
            if (JumpRunsFinallyReattach(
                    exit, scan, local, saveContext, receiverPath,
                    model, localFunctionSyntax))
                continue;
            return true;
        }

        return false;
    }

    private static bool NestedGotoSkipsPosition(
        GotoStatementSyntax gotoStatement,
        int position,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // Closed-form forward rule only: a nested backward jump is treated as
        // an exit rather than recursing.
        if (gotoStatement.Expression is not IdentifierNameSyntax identifier)
            return true;
        SyntaxNode? target = null;
        foreach (var label in localFunctionSyntax.DescendantNodes().OfType<LabeledStatementSyntax>())
        {
            if (label.Identifier.ValueText != identifier.Identifier.ValueText)
                continue;
            var boundary = label.Ancestors().FirstOrDefault(ancestor =>
                ancestor is LocalFunctionStatementSyntax
                    or LambdaExpressionSyntax
                    or AnonymousMethodExpressionSyntax);
            if (!ReferenceEquals(boundary, localFunctionSyntax))
                continue;
            if (target != null)
                return true;
            target = label;
        }

        return target != null &&
            gotoStatement.SpanStart < position &&
            position < target.SpanStart;
    }

    private static bool JumpRunsFinallyReattach(
        SyntaxNode jump,
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // A transfer out of a try whose finally reattaches still persists the
        // mutation while unwinding: the reattachment runs before the jump
        // completes.
        if (!scan.ReattachesByLocal.TryGetValue(local, out var reattaches))
            return false;
        foreach (var tryStatement in jump.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!localFunctionSyntax.Span.Contains(tryStatement.Span))
                break;
            if (tryStatement.Finally?.Block is not { } finallyBlock)
                continue;
            foreach (var candidate in reattaches)
            {
                if (!candidate.PersistsExistingMutation)
                    continue;
                if (!finallyBlock.Span.Contains(candidate.Operation.Syntax.Span))
                    continue;
                if (!IsDirectCalleeOperation(candidate.Operation.Syntax, localFunctionSyntax))
                    continue;
                if (candidate.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(candidate.ContextSymbol, saveContext))
                    continue;
                if (!ReattachCoversPath(candidate, receiverPath))
                    continue;
                if (!IsUnconditionalWithin(candidate.Operation.Syntax, finallyBlock))
                    continue;
                if (CalleeInvalidatedBetween(
                        scan, local, saveContext, receiverPath,
                        candidate.SpanStart, finallyBlock.Span.End, model, localFunctionSyntax))
                    continue;
                return true;
            }
        }

        return false;
    }




    private static bool BranchesAreMutuallyExclusive(
        SyntaxNode invalidator,
        int mutationSpan,
        SyntaxNode boundary)
    {
        // An invalidation in a different arm of the same if/else (or a different
        // section of the same switch) than the mutation can never execute on the
        // mutation's path.
        foreach (var ifStatement in invalidator.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!boundary.Span.Contains(ifStatement.Span))
                break;

            var invalidArm = BranchArm(ifStatement, invalidator.SpanStart);
            var mutationArm = BranchArm(ifStatement, mutationSpan);
            if (invalidArm != 0 && mutationArm != 0 && invalidArm != mutationArm)
                return true;
        }

        foreach (var switchStatement in invalidator.Ancestors().OfType<SwitchStatementSyntax>())
        {
            if (!boundary.Span.Contains(switchStatement.Span))
                break;

            SwitchSectionSyntax? invalidSection = null;
            SwitchSectionSyntax? mutationSection = null;
            foreach (var section in switchStatement.Sections)
            {
                if (section.Span.Contains(invalidator.SpanStart))
                    invalidSection = section;
                if (section.Span.Contains(mutationSpan))
                    mutationSection = section;
            }

            if (invalidSection != null && mutationSection != null && invalidSection != mutationSection)
                return true;
        }

        return false;
    }

    private static bool InvalidationCannotReachMutation(
        SyntaxNode invalidation,
        int mutationSpan,
        SemanticModel? model,
        SyntaxNode boundary)
    {
        // An invalidation inside an if-branch arm that never falls through
        // never reaches code past the branch. Before the mutation, both
        // returns and throw-shapes divert (the mutation is skipped or the
        // save unreachable). After it, only throw-shapes void the save: a
        // return still reaches the caller's save with an untracked change.
        var preMutation = invalidation.SpanStart < mutationSpan;
        foreach (var ifStatement in invalidation.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!boundary.Span.Contains(ifStatement.Span))
                break;
            var which = BranchArm(ifStatement, invalidation.SpanStart);
            if (which == 0)
                continue;
            var arm = which == 1 ? ifStatement.Statement : ifStatement.Else?.Statement;
            if (arm == null || arm.Span.Contains(mutationSpan))
                continue;

            var last = arm is BlockSyntax armBlock && armBlock.Statements.Count > 0
                ? armBlock.Statements[armBlock.Statements.Count - 1]
                : arm;
            if (StatementNeverCompletesNormally(last, model))
                return true;
            if (preMutation && last is ReturnStatementSyntax)
                return true;
        }

        return false;
    }
    private static bool MutationPathSkipsInvalidation(
        SyntaxNode mutationSyntax,
        int mutationSpan,
        SyntaxNode invalidation,
        SemanticModel? model,
        SyntaxNode boundary)
    {
        // A post-mutation invalidation the mutation path never reaches does
        // not void the save: the mutation arm returns out of the branch, so
        // only paths that never mutated flow into the invalidation.
        if (invalidation.SpanStart <= mutationSpan)
            return false;
        foreach (var ifStatement in mutationSyntax.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!boundary.Span.Contains(ifStatement.Span))
                break;
            var which = BranchArm(ifStatement, mutationSpan);
            if (which == 0)
                continue;
            var arm = which == 1 ? ifStatement.Statement : ifStatement.Else?.Statement;
            if (arm == null || arm.Span.Contains(invalidation.SpanStart))
                continue;
            var last = arm is BlockSyntax armBlock && armBlock.Statements.Count > 0
                ? armBlock.Statements[armBlock.Statements.Count - 1]
                : arm;
            if (last is ReturnStatementSyntax || StatementNeverCompletesNormally(last, model))
                return true;
        }

        return false;
    }


    private static int BranchArm(IfStatementSyntax ifStatement, int position)
    {
        if (ifStatement.Statement != null && ifStatement.Statement.Span.Contains(position))
            return 1;
        if (ifStatement.Else != null && ifStatement.Else.Span.Contains(position))
            return 2;
        return 0;
    }

    private static bool CalleeRangeUntracks(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        ImmutableArray<MemberPathSegment> receiverPath,
        int startSpan,
        int endSpan,
        int mutationSpan,
        SyntaxNode mutationSyntax,
        SemanticModel? model,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        if (scan.DetachesByLocal.TryGetValue(local, out var detaches))
        {
            foreach (var detach in detaches)
            {
                if (!localFunctionSyntax.Span.Contains(detach.Operation.Syntax.Span))
                    continue;
                if (!IsDirectCalleeOperation(detach.Operation.Syntax, localFunctionSyntax))
                    continue;
                if (detach.SpanStart <= startSpan || detach.SpanStart >= endSpan)
                    continue;
                if (BranchesAreMutuallyExclusive(
                        detach.Operation.Syntax, mutationSpan, localFunctionSyntax))
                    continue;
                if (InvalidationCannotReachMutation(
                        detach.Operation.Syntax, mutationSpan, model, localFunctionSyntax))
                    continue;
                if (MutationPathSkipsInvalidation(
                        mutationSyntax, mutationSpan, detach.Operation.Syntax, model, localFunctionSyntax))
                    continue;
                if (DeadGuardEncloses(
                        detach.Operation.Syntax, detach.SpanStart, model, localFunctionSyntax))
                    continue;
                if (detach.ContextSymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(detach.ContextSymbol, saveContext))
                    continue;
                // Mirror HasInterveningDetach: only a detach of the exact mutated
                // path invalidates tracking, not a detach of an ancestor.
                if (detach.TargetPath.Length == receiverPath.Length &&
                    MemberPathIsPrefix(detach.TargetPath, receiverPath))
                    return true;
            }
        }

        foreach (var clear in scan.TrackerClears)
        {
            if (!localFunctionSyntax.Span.Contains(clear.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(clear.Operation.Syntax, localFunctionSyntax))
                continue;
            if (clear.SpanStart <= startSpan || clear.SpanStart >= endSpan)
                continue;
            if (BranchesAreMutuallyExclusive(
                clear.Operation.Syntax, mutationSpan, localFunctionSyntax))
                continue;
            if (InvalidationCannotReachMutation(
                    clear.Operation.Syntax, mutationSpan, model, localFunctionSyntax))
                continue;
            if (MutationPathSkipsInvalidation(
                    mutationSyntax, mutationSpan, clear.Operation.Syntax, model, localFunctionSyntax))
                continue;
            if (DeadGuardEncloses(
                    clear.Operation.Syntax, clear.SpanStart, model, localFunctionSyntax))
                continue;
            if (clear.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(clear.ContextSymbol, saveContext))
                continue;
            return true;
        }

        return false;
    }

    private static bool CalleeInnerSavePersists(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        var mutationSpan = entry.Operation.Syntax.SpanStart;
        var model = entry.Operation.SemanticModel;
        foreach (var saveCall in scan.SaveChangesCalls)
        {
            var saveSyntax = saveCall.Operation.Syntax;
            if (!localFunctionSyntax.Span.Contains(saveSyntax.Span))
                continue;
            if (!IsDirectCalleeOperation(saveSyntax, localFunctionSyntax))
                continue;
            if (saveCall.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(saveCall.ContextSymbol, saveContext))
                continue;
            if (saveCall.SpanStart <= mutationSpan)
                continue;
            if (DeadGuardEncloses(saveSyntax, saveCall.SpanStart, model, localFunctionSyntax))
                continue;
            if (!BlockReaches(entry.Operation, saveCall.Operation))
                continue;
            // Tracked from the reattachment through this save: invalidations
            // after the write are moot.
            if (CalleeReattachPersistsMutation(
                    scan, local, saveContext, entry, localFunctionSyntax,
                    persistThroughSpan: saveCall.SpanStart))
                return true;
        }

        return false;
    }

    private static bool CalleeSavesAfterMutation(
        AsNoTrackingThenModifyRootScan scan,
        ILocalSymbol local,
        ISymbol saveContext,
        MutationEntry entry,
        LocalFunctionStatementSyntax localFunctionSyntax)
    {
        // The inner save can only have reported the mutation when the entity
        // origin itself is visible inside the callee: a caller-declared local
        // is invisible to the inner analysis, so only callee-declared origins
        // suppress the outer diagnostic.
        var declaredInside = scan.InitializedDeclarators.Any(declarator =>
                SymbolEqualityComparer.Default.Equals(declarator.Symbol, local)
                && localFunctionSyntax.Span.Contains(declarator.Syntax.Span))
            || scan.ForEachLoops.Any(loop =>
                loop.Locals.Any(loopLocal =>
                    SymbolEqualityComparer.Default.Equals(loopLocal, local)
                    && localFunctionSyntax.Span.Contains(loop.Syntax.Span)));
        if (!declaredInside)
            return false;

        var mutationSpan = entry.Operation.Syntax.SpanStart;
        foreach (var saveCall in scan.SaveChangesCalls)
        {
            if (!localFunctionSyntax.Span.Contains(saveCall.Operation.Syntax.Span))
                continue;
            if (!IsDirectCalleeOperation(saveCall.Operation.Syntax, localFunctionSyntax))
                continue;
            if (saveCall.ContextSymbol == null ||
                !SymbolEqualityComparer.Default.Equals(saveCall.ContextSymbol, saveContext))
                continue;

            // A same-context save after the mutation inside the callee already
            // reports this mutation; the caller's save must not report it again.
            // (A save before the mutation cannot have reported it.)
            if (saveCall.SpanStart > mutationSpan)
                return true;
        }

        return false;
    }

    private static bool IsAwaited(IOperation invocation)
    {
        var current = invocation;
        while (true)
        {
            while ((current.Parent is IConversionOperation conversion &&
                    ReferenceEquals(conversion.Operand, current)) ||
                   (current.Parent is IParenthesizedOperation parenthesized &&
                    ReferenceEquals(parenthesized.Operand, current)))
            {
                current = current.Parent;
            }

            // `await task.ConfigureAwait(false)` awaits the invocation through
            // the configured-await wrapper: keep unwrapping to the await.
            if (current.Parent is IInvocationOperation wrapper
                && wrapper.TargetMethod.Name == "ConfigureAwait"
                && wrapper.Arguments.Length > 0
                && ReferenceEquals(wrapper.Instance?.UnwrapConversions(), current))
            {
                current = wrapper;
                continue;
            }

            break;
        }

        return current.Parent is IAwaitOperation;
    }

    private static bool HasMultipleAssignments(IOperation root, ILocalSymbol local)
    {
        var assignments = LocalAssignmentCache.GetAssignments(root, local);
        return assignments.Count > 1;
    }
}
