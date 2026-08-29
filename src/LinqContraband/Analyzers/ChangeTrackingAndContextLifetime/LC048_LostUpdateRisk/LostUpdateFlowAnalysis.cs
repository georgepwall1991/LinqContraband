using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC048_LostUpdateRisk;

internal static class LostUpdateFlowAnalysis
{
    internal static void Analyze(
        OperationBlockAnalysisContext context,
        LostUpdateCompilationEvidence evidence,
        DiagnosticDescriptor rule
    )
    {
        if (context.OwningSymbol is not IMethodSymbol method || context.OperationBlocks.IsEmpty)
            return;

        var collector = new OperationCollector();
        foreach (var block in context.OperationBlocks)
            collector.Visit(block);

        if (!HasPotentialAnalysisCandidate(collector))
            return;

        var executableRoot = context.OperationBlocks[0].FindOwningExecutableRoot();
        var flowGraph = TryCreateFlowGraph(executableRoot, context.CancellationToken);

        var unstableLocals = GetReassignedLocals(collector);
        var contexts = new Dictionary<ILocalSymbol, ISymbol>(SymbolEqualityComparer.Default);
        var queries = new Dictionary<ILocalSymbol, QuerySource>(SymbolEqualityComparer.Default);
        var entities = new Dictionary<ILocalSymbol, EntitySource>(SymbolEqualityComparer.Default);

        BuildOrigins(collector, unstableLocals, contexts, queries, entities, flowGraph);
        var loadedValues = new LoadedValueAnalysis(
            collector,
            unstableLocals,
            contexts,
            entities,
            flowGraph
        );

        var mutations = new List<MutationEvidence>();
        foreach (var compound in collector.CompoundAssignments)
            AddDirectMutation(
                compound.Target,
                compound,
                compound.Syntax.SpanStart,
                contexts,
                queries,
                entities,
                collector,
                flowGraph,
                mutations
            );
        foreach (var coalesce in collector.CoalesceAssignments)
            AddDirectMutation(
                coalesce.Target,
                coalesce,
                coalesce.Syntax.SpanStart,
                contexts,
                queries,
                entities,
                collector,
                flowGraph,
                mutations
            );

        foreach (var increment in collector.Increments)
            AddDirectMutation(
                increment.Target,
                increment,
                increment.Syntax.SpanStart,
                contexts,
                queries,
                entities,
                collector,
                flowGraph,
                mutations
            );

        foreach (var assignment in collector.SimpleAssignments)
        {
            if (
                assignment.Target is not IPropertyReferenceOperation target
                || !TryResolveEntity(
                    target.Instance,
                    contexts,
                    queries,
                    entities,
                    collector,
                    flowGraph,
                    out var entity
                )
                || !LostUpdateOperationFacts.IsScalarProperty(target.Property)
            )
            {
                continue;
            }

            if (
                loadedValues.Contains(assignment.Value, target.Property, entity)
                || IsGuardedByEntityPropertyRead(
                    assignment,
                    target.Property,
                    entity,
                    entities,
                    flowGraph,
                    loadedValues
                )
            )
            {
                mutations.Add(
                    new MutationEvidence(
                        entity,
                        target.Property,
                        target.Syntax.GetLocation(),
                        assignment.Syntax.SpanStart,
                        assignment,
                        isPlainSelfAssignment: IsPlainSelfAssignment(
                            assignment,
                            target,
                            entity,
                            contexts,
                            queries,
                            entities,
                            collector,
                            flowGraph
                        )
                    )
                );
            }
        }

        var saves = new List<SaveEvidence>();
        var callerTree = context.OperationBlocks[0].Syntax.SyntaxTree;
        var transactions = new List<TransactionEvidence>();
        var transactionResets = new List<TransactionResetEvidence>();
        foreach (var invocation in collector.Invocations)
        {
            if (
                LostUpdateOperationFacts.IsTransactionOperation(invocation)
                && IsObservedTransactionOperation(invocation)
                && !IsImmediatelyTerminatedTransaction(invocation)
                && TryResolveTransactionContext(invocation, contexts, out var transactionContext)
            )
            {
                var argumentState = LostUpdateOperationFacts.IsUseTransactionOperation(invocation)
                    ? GetUseTransactionArgumentNullState(invocation, collector, flowGraph)
                    : TransactionArgumentNullState.NonNull;
                if (argumentState == TransactionArgumentNullState.NonNull)
                {
                    transactions.Add(
                        new TransactionEvidence(
                            transactionContext,
                            invocation.Syntax.SpanStart,
                            invocation
                        )
                    );
                }
                else if (argumentState == TransactionArgumentNullState.Null)
                {
                    transactionResets.Add(
                        new TransactionResetEvidence(transactionContext, invocation)
                    );
                }
            }

            if (
                LostUpdateOperationFacts.IsSaveChanges(invocation)
                && TryResolveContext(invocation.Instance, contexts, out var saveContext)
            )
            {
                saves.Add(
                    new SaveEvidence(
                        saveContext,
                        invocation.Syntax.GetLocation(),
                        invocation.Syntax.SpanStart,
                        invocation,
                        contextAccess: TryGetContextAccessSymbol(
                            invocation.Instance,
                            out var saveAccess
                        )
                            ? saveAccess
                            : saveContext
                    )
                );
            }

            if (!evidence.TryGetHelperSummary(invocation, callerTree, out var helper))
                continue;

            foreach (var helperTransaction in helper.TransactionEffects)
            {
                if (
                    TryResolveHelperContext(
                        helperTransaction.Target,
                        invocation,
                        contexts,
                        out var helperTransactionContext
                    )
                    && TryCreateHelperTransactionEvidence(
                        invocation,
                        helperTransaction,
                        helperTransactionContext,
                        out var transaction
                    )
                )
                {
                    transactions.Add(transaction);
                }
            }

            foreach (var helperSave in helper.SaveEffects)
            {
                if (
                    IsHelperSaveExecuted(invocation, helperSave)
                    && TryResolveHelperContext(
                        helperSave.Target,
                        invocation,
                        contexts,
                        out var helperSaveContext
                    )
                )
                {
                    saves.Add(
                        new SaveEvidence(
                            helperSaveContext,
                            helperSave.Location,
                            invocation.Syntax.SpanStart,
                            invocation,
                            helperSave.Position,
                            TryGetHelperContextAccessSymbol(
                                helperSave.Target,
                                invocation,
                                out var helperSaveAccess
                            )
                                ? helperSaveAccess
                                : helperSaveContext
                        )
                    );
                }
            }

            foreach (var helperMutation in helper.Mutations)
            {
                if (
                    IsHelperMutationPossible(invocation, helperMutation)
                    && TryResolveHelperEntity(
                        helperMutation.Target,
                        invocation,
                        entities,
                        out var helperEntity
                    )
                    && LostUpdateOperationFacts.IsScalarProperty(helperMutation.Property)
                )
                {
                    var containedSaveContexts = ImmutableHashSet.CreateBuilder<ISymbol>(
                        SymbolEqualityComparer.Default
                    );
                    foreach (var containedSave in helperMutation.SubsequentSaves)
                    {
                        if (
                            IsHelperSaveExecuted(invocation, containedSave)
                            && TryResolveHelperContext(
                                containedSave.Target,
                                invocation,
                                contexts,
                                out var containedSaveContext
                            )
                        )
                        {
                            containedSaveContexts.Add(containedSaveContext);
                        }
                    }

                    mutations.Add(
                        new MutationEvidence(
                            helperEntity,
                            helperMutation.Property,
                            helperMutation.Location,
                            invocation.Syntax.SpanStart,
                            invocation,
                            containedSaveContexts.ToImmutable()
                        )
                    );
                }
            }
        }
        var materializedEntities = entities
            .Values.Concat(mutations.Select(mutation => mutation.Entity))
            .Distinct();
        ApplyDefaultContextTrackingBehavior(
            evidence,
            materializedEntities,
            context.CancellationToken
        );
        ApplyContextTrackingBehavior(
            collector.SimpleAssignments,
            contexts,
            materializedEntities,
            flowGraph
        );

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mutation in mutations.OrderBy(item => item.Position))
        {
            if (
                IsDefinitelyInitializedBeforeMutation(
                    mutation,
                    collector.SimpleAssignments,
                    collector.Invocations,
                    contexts,
                    entities,
                    flowGraph,
                    loadedValues
                )
            )
            {
                continue;
            }

            if (
                evidence.IsKeylessEntity(
                    mutation.Entity.EntityType,
                    mutation.Entity.Context,
                    context.CancellationToken
                )
            )
            {
                continue;
            }

            if (
                evidence.IsIgnoredProperty(
                    mutation.Entity.EntityType,
                    mutation.Property,
                    mutation.Entity.Context,
                    context.CancellationToken
                )
            )
            {
                continue;
            }
            if (
                evidence.IsStoreGeneratedProperty(
                    mutation.Entity.EntityType,
                    mutation.Property,
                    mutation.Entity.Context,
                    context.CancellationToken
                )
            )
            {
                continue;
            }
            if (IsPrimaryKeyProperty(mutation, evidence, context.CancellationToken))
                continue;
            if (
                evidence.TryGetFluentAlternateKeys(
                    mutation.Entity.EntityType,
                    mutation.Entity.Context,
                    context.CancellationToken,
                    out var alternateKeys
                ) && alternateKeys.Contains(mutation.Property.OriginalDefinition)
            )
            {
                continue;
            }

            if (
                evidence.HasConcurrencyProtection(
                    mutation.Entity.EntityType,
                    mutation.Property,
                    mutation.Entity.Context,
                    context.CancellationToken
                )
            )
            {
                continue;
            }

            var save = saves
                .Where(candidate =>
                    SymbolEqualityComparer.Default.Equals(
                        candidate.Context,
                        mutation.Entity.Context
                    )
                    && IsContextStableForSave(mutation.Entity, candidate, collector, flowGraph)
                    && (
                        IsEntityTrackedOnPath(
                            mutation,
                            candidate,
                            collector.SimpleAssignments,
                            contexts,
                            collector,
                            flowGraph
                        )
                        || HasReattachmentBeforeSave(
                            mutation,
                            candidate,
                            collector.Invocations,
                            contexts,
                            queries,
                            entities,
                            flowGraph
                        )
                        || HasEntryStatePersistenceBeforeSave(
                            mutation,
                            candidate,
                            collector.SimpleAssignments,
                            contexts,
                            entities,
                            flowGraph
                        )
                    )
                    && (
                        !mutation.IsPlainSelfAssignment
                        || evidence.HasIndependentChangeDetection(
                            mutation.Entity.Context,
                            mutation.Entity.EntityType,
                            context.CancellationToken
                        )
                        || HasExplicitSetterPersistenceBeforeSave(
                            mutation,
                            candidate,
                            collector.SimpleAssignments,
                            collector.Invocations,
                            contexts,
                            queries,
                            entities,
                            flowGraph
                        )
                    )
                    && !IsDefinitelyDetachedBeforeSave(
                        mutation,
                        candidate,
                        collector.SimpleAssignments,
                        collector.Invocations,
                        contexts,
                        queries,
                        entities,
                        flowGraph
                    )
                    && !IsDefinitelyOverwrittenBeforeSave(
                        mutation,
                        candidate,
                        collector.SimpleAssignments,
                        entities,
                        flowGraph,
                        loadedValues
                    )
                    && (
                        evidence.HasIndependentChangeDetection(
                            mutation.Entity.Context,
                            mutation.Entity.EntityType,
                            context.CancellationToken
                        )
                        || !IsAutoDetectionDisabledBeforeSave(
                            mutation,
                            candidate,
                            collector.SimpleAssignments,
                            collector.Invocations,
                            contexts,
                            queries,
                            entities,
                            flowGraph
                        )
                    )
                    && CanFlowToSave(mutation, candidate, collector, flowGraph)
                    && !IsProtectedByTransaction(
                        mutation,
                        candidate,
                        transactions,
                        transactionResets,
                        collector,
                        evidence,
                        callerTree,
                        flowGraph
                    )
                )
                .OrderBy(candidate => candidate.Position)
                .FirstOrDefault();

            if (save.Location == null)
                continue;

            var key =
                mutation.Location.SourceTree?.FilePath
                + ":"
                + mutation.Location.SourceSpan.Start
                + ":"
                + save.Location.SourceSpan.Start;
            if (!reported.Add(key))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    rule,
                    mutation.Location,
                    additionalLocations: ImmutableArray.Create(save.Location),
                    properties: null,
                    mutation.Property.Name
                )
            );
        }
    }

    private static bool HasPotentialAnalysisCandidate(OperationCollector collector)
    {
        if (
            !collector.Invocations.Any(invocation =>
                LostUpdateOperationFacts.IsSingleEntityTerminal(invocation.TargetMethod)
            )
        )
        {
            return false;
        }

        var hasSourceHelper = collector.Invocations.Any(invocation =>
            !invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty
        );
        var hasPotentialMutation =
            collector.SimpleAssignments.Any(assignment =>
                LostUpdateOperationFacts.Unwrap(assignment.Target) is IPropertyReferenceOperation
            )
            || collector.CompoundAssignments.Count != 0
            || collector.CoalesceAssignments.Count != 0
            || collector.Increments.Count != 0
            || hasSourceHelper;
        if (!hasPotentialMutation)
            return false;

        return hasSourceHelper || collector.Invocations.Any(LostUpdateOperationFacts.IsSaveChanges);
    }

    private static HashSet<ISymbol> GetUnstableContextParameters(OperationCollector collector)
    {
        var result = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var assignment in collector.SimpleAssignments)
        {
            if (
                LostUpdateOperationFacts.Unwrap(assignment.Target)
                    is IParameterReferenceOperation parameter
                && LostUpdateOperationFacts.IsDbContextType(parameter.Parameter.Type)
            )
            {
                result.Add(parameter.Parameter);
            }
        }

        foreach (var invocation in collector.Invocations)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && LostUpdateOperationFacts.Unwrap(argument.Value)
                        is IParameterReferenceOperation parameter
                    && LostUpdateOperationFacts.IsDbContextType(parameter.Parameter.Type)
                )
                {
                    result.Add(parameter.Parameter);
                }
            }
        }

        return result;
    }

    private static HashSet<ILocalSymbol> GetReassignedLocals(OperationCollector collector)
    {
        var result = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var assignment in collector.SimpleAssignments)
        {
            if (
                LostUpdateOperationFacts.Unwrap(assignment.Target) is ILocalReferenceOperation local
            )
                result.Add(local.Local);
        }

        foreach (var compound in collector.CompoundAssignments)
        {
            if (LostUpdateOperationFacts.Unwrap(compound.Target) is ILocalReferenceOperation local)
                result.Add(local.Local);
        }

        foreach (var coalesce in collector.CoalesceAssignments)
        {
            if (LostUpdateOperationFacts.Unwrap(coalesce.Target) is ILocalReferenceOperation local)
                result.Add(local.Local);
        }

        foreach (var increment in collector.Increments)
        {
            if (LostUpdateOperationFacts.Unwrap(increment.Target) is ILocalReferenceOperation local)
                result.Add(local.Local);
        }

        foreach (var invocation in collector.Invocations)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && LostUpdateOperationFacts.Unwrap(argument.Value)
                        is ILocalReferenceOperation local
                )
                {
                    result.Add(local.Local);
                }
            }
        }

        return result;
    }

    private static void BuildOrigins(
        OperationCollector collector,
        HashSet<ILocalSymbol> unstableLocals,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        var remaining = collector
            .Declarators.Where(declaration => declaration.Initializer?.Value != null)
            .ToList();

        var madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;
            for (var index = remaining.Count - 1; index >= 0; index--)
            {
                var declaration = remaining[index];
                var value = declaration.Initializer!.Value;

                if (TryResolveContext(value, contexts, out var contextSymbol))
                {
                    contexts[declaration.Symbol] = contextSymbol;
                    remaining.RemoveAt(index);
                    madeProgress = true;
                    continue;
                }

                if (unstableLocals.Contains(declaration.Symbol))
                {
                    remaining.RemoveAt(index);
                    madeProgress = true;
                    continue;
                }
                if (TryResolveEntity(value, entities, out var entityAlias))
                {
                    entities[declaration.Symbol] = entityAlias;
                    remaining.RemoveAt(index);
                    madeProgress = true;
                    continue;
                }

                if (
                    TryResolveMaterialization(
                        value,
                        contexts,
                        queries,
                        collector,
                        flowGraph,
                        out var materialized
                    )
                )
                {
                    entities[declaration.Symbol] = materialized;
                    remaining.RemoveAt(index);
                    madeProgress = true;
                    continue;
                }

                if (TryResolveQuery(value, contexts, queries, out var query))
                {
                    queries[declaration.Symbol] = query;
                    remaining.RemoveAt(index);
                    madeProgress = true;
                }
            }
        }
    }

    private static void ApplyDefaultContextTrackingBehavior(
        LostUpdateCompilationEvidence evidence,
        IEnumerable<EntitySource> entities,
        CancellationToken cancellationToken
    )
    {
        foreach (var entity in entities)
        {
            if (
                entity.IsTracked
                && entity.HonorsContextTrackingBehavior
                && evidence.IsDefaultNoTracking(entity.Context, cancellationToken)
            )
            {
                entity.IsTracked = false;
            }
        }
    }

    private static void ApplyContextTrackingBehavior(
        IEnumerable<ISimpleAssignmentOperation> assignments,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        IEnumerable<EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return;

        foreach (var entity in entities)
        {
            if (!entity.HonorsContextTrackingBehavior)
                continue;

            var relevantAssignments = assignments
                .Where(assignment =>
                    TryGetQueryTrackingBehaviorAssignment(
                        assignment,
                        contexts,
                        out var assignmentContext,
                        out _
                    ) && SymbolEqualityComparer.Default.Equals(assignmentContext, entity.Context)
                )
                .ToArray();
            var latest = relevantAssignments
                .Where(assignment =>
                    assignment.Syntax.SpanStart < entity.MaterializationPosition
                    && OperationDominates(assignment, entity.Materialization, flowGraph)
                )
                .OrderByDescending(assignment => assignment.Syntax.SpanStart)
                .FirstOrDefault();

            if (latest == null)
            {
                var promotions = relevantAssignments
                    .Where(assignment =>
                        TryGetQueryTrackingBehaviorAssignment(
                            assignment,
                            contexts,
                            out _,
                            out var behavior
                        )
                        && behavior is not ("NoTracking" or "NoTrackingWithIdentityResolution")
                        && OperationCanReach(assignment, entity.Materialization, flowGraph)
                    )
                    .ToImmutableArray();
                if (!promotions.IsEmpty)
                {
                    if (!entity.IsTracked)
                        entity.TrackingPromotions = promotions;
                    entity.IsTracked = true;
                }

                continue;
            }

            TryGetQueryTrackingBehaviorAssignment(latest, contexts, out _, out var latestBehavior);
            if (latestBehavior is not ("NoTracking" or "NoTrackingWithIdentityResolution"))
            {
                entity.IsTracked = true;
                continue;
            }

            var laterPromotions = relevantAssignments
                .Where(assignment =>
                    assignment.Syntax.SpanStart > latest.Syntax.SpanStart
                    && TryGetQueryTrackingBehaviorAssignment(
                        assignment,
                        contexts,
                        out _,
                        out var laterBehavior
                    )
                    && laterBehavior is not ("NoTracking" or "NoTrackingWithIdentityResolution")
                    && OperationCanReachWithoutPassingThrough(
                        assignment,
                        entity.Materialization,
                        latest,
                        flowGraph
                    )
                )
                .ToImmutableArray();
            entity.IsTracked = !laterPromotions.IsEmpty;
            entity.TrackingPromotions = laterPromotions;
        }
    }

    private static bool IsEntityTrackedOnPath(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        var entity = mutation.Entity;
        if (!entity.IsTracked || !entity.HonorsContextTrackingBehavior || flowGraph == null)
            return entity.IsTracked;

        if (
            !entity.TrackingPromotions.IsDefaultOrEmpty
            && !entity.TrackingPromotions.Any(assignment =>
                IsTrackingPromotionCompatibleWithPath(
                    assignment,
                    mutation,
                    save,
                    collector,
                    flowGraph
                )
            )
        )
        {
            return false;
        }

        var relevantAssignments = assignments
            .Where(assignment =>
                assignment.Syntax.SpanStart < entity.MaterializationPosition
                && TryGetQueryTrackingBehaviorAssignment(
                    assignment,
                    contexts,
                    out var assignmentContext,
                    out _
                )
                && SymbolEqualityComparer.Default.Equals(assignmentContext, entity.Context)
            )
            .ToArray();

        foreach (var assignment in relevantAssignments)
        {
            TryGetQueryTrackingBehaviorAssignment(assignment, contexts, out _, out var behavior);
            if (behavior is not ("NoTracking" or "NoTrackingWithIdentityResolution"))
                continue;

            foreach (var predicate in GetRequiredBooleanPredicates(assignment))
            {
                if (
                    !IsBooleanSymbolStableBetween(
                        predicate.Symbol,
                        assignment.Syntax.SpanStart,
                        save.Position,
                        collector
                    )
                    || !OperationRequiresBooleanPredicate(mutation.Operation, predicate)
                    || !OperationCanReach(
                        assignment,
                        entity.Materialization,
                        predicate.Symbol,
                        predicate.Value,
                        flowGraph
                    )
                )
                {
                    continue;
                }

                var isResetBeforeMaterialization = relevantAssignments.Any(other =>
                {
                    if (
                        ReferenceEquals(other, assignment)
                        || other.Syntax.SpanStart <= assignment.Syntax.SpanStart
                        || other.Syntax.SpanStart >= entity.MaterializationPosition
                    )
                    {
                        return false;
                    }

                    TryGetQueryTrackingBehaviorAssignment(
                        other,
                        contexts,
                        out _,
                        out var laterBehavior
                    );
                    return laterBehavior is not ("NoTracking" or "NoTrackingWithIdentityResolution")
                        && OperationCanReach(
                            assignment,
                            other,
                            predicate.Symbol,
                            predicate.Value,
                            flowGraph
                        )
                        && OperationCanReach(
                            other,
                            entity.Materialization,
                            predicate.Symbol,
                            predicate.Value,
                            flowGraph
                        );
                });
                if (!isResetBeforeMaterialization)
                    return false;
            }
        }

        return true;
    }

    private static bool IsTrackingPromotionCompatibleWithPath(
        ISimpleAssignmentOperation assignment,
        MutationEvidence mutation,
        SaveEvidence save,
        OperationCollector collector,
        ControlFlowGraph flowGraph
    )
    {
        foreach (var predicate in GetRequiredBooleanPredicates(assignment))
        {
            if (
                !IsBooleanSymbolStableBetween(
                    predicate.Symbol,
                    assignment.Syntax.SpanStart,
                    save.Position,
                    collector
                )
            )
            {
                continue;
            }

            if (
                !OperationCanReach(
                    assignment,
                    mutation.Operation,
                    predicate.Symbol,
                    predicate.Value,
                    flowGraph
                )
                || !OperationCanReach(
                    assignment,
                    save.Invocation,
                    predicate.Symbol,
                    predicate.Value,
                    flowGraph
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetQueryTrackingBehaviorAssignment(
        ISimpleAssignmentOperation assignment,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out ISymbol context,
        out string behavior
    )
    {
        if (
            assignment.Target
                is IPropertyReferenceOperation
                {
                    Property: { } behaviorProperty,
                    Instance: { } behaviorReceiver,
                }
            && LostUpdateOperationFacts.IsEfChangeTrackerProperty(
                behaviorProperty,
                "QueryTrackingBehavior"
            )
            && LostUpdateOperationFacts.Unwrap(behaviorReceiver)
                is IPropertyReferenceOperation
                {
                    Property: { } changeTrackerProperty,
                    Instance: { } contextInstance,
                }
            && LostUpdateOperationFacts.IsEfDbContextChangeTrackerProperty(changeTrackerProperty)
            && TryResolveContext(contextInstance, contexts, out context)
        )
        {
            var value = LostUpdateOperationFacts.Unwrap(assignment.Value);
            if (
                value
                    is IFieldReferenceOperation
                    {
                        Field:
                        {
                            Name: var behaviorName,
                            ContainingType:
                            {
                                Name: "QueryTrackingBehavior",
                                ContainingNamespace: { } behaviorNamespace,
                            },
                        },
                    }
                && behaviorNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore"
            )
            {
                behavior = behaviorName;
                return true;
            }

            behavior = string.Empty;
            return true;
        }

        context = null!;
        behavior = string.Empty;
        return false;
    }

    private static bool OperationDominates(
        IOperation operation,
        IOperation target,
        ControlFlowGraph flowGraph
    )
    {
        var operationBlock = FindContainingBlock(flowGraph, operation);
        var targetBlock = FindContainingBlock(flowGraph, target);
        if (operationBlock == null || targetBlock == null)
            return false;

        if (ReferenceEquals(operationBlock, targetBlock))
        {
            return operation.Syntax.SpanStart < target.Syntax.SpanStart
                && CanReach(flowGraph.Blocks[0], targetBlock);
        }

        return CanReach(flowGraph.Blocks[0], operationBlock)
            && CanReach(operationBlock, targetBlock)
            && !CanReachAvoiding(flowGraph.Blocks[0], targetBlock, operationBlock);
    }

    private static bool OperationCanReach(
        IOperation operation,
        IOperation target,
        ControlFlowGraph flowGraph
    )
    {
        var operationBlock = FindContainingBlock(flowGraph, operation);
        var targetBlock = FindContainingBlock(flowGraph, target);
        if (operationBlock == null || targetBlock == null)
            return false;

        if (ReferenceEquals(operationBlock, targetBlock))
            return operation.Syntax.SpanStart < target.Syntax.SpanStart;

        return CanReach(flowGraph.Blocks[0], operationBlock)
            && CanReach(operationBlock, targetBlock);
    }

    private static bool OperationCanReach(
        IOperation operation,
        IOperation target,
        ISymbol assumedSymbol,
        bool assumedValue,
        ControlFlowGraph flowGraph
    )
    {
        var operationBlock = FindContainingBlock(flowGraph, operation);
        var targetBlock = FindContainingBlock(flowGraph, target);
        if (operationBlock == null || targetBlock == null)
            return false;

        if (ReferenceEquals(operationBlock, targetBlock))
            return operation.Syntax.SpanStart < target.Syntax.SpanStart;

        return CanReach(flowGraph.Blocks[0], operationBlock, assumedSymbol, assumedValue)
            && CanReach(operationBlock, targetBlock, assumedSymbol, assumedValue);
    }

    private static bool OperationCanReachWithoutPassingThrough(
        IOperation operation,
        IOperation target,
        IOperation reset,
        ControlFlowGraph flowGraph
    )
    {
        var operationBlock = FindContainingBlock(flowGraph, operation);
        var targetBlock = FindContainingBlock(flowGraph, target);
        var resetBlock = FindContainingBlock(flowGraph, reset);
        if (operationBlock == null || targetBlock == null || resetBlock == null)
            return false;
        if (!CanReach(flowGraph.Blocks[0], operationBlock))
            return false;

        if (ReferenceEquals(operationBlock, targetBlock))
        {
            if (operation.Syntax.SpanStart < target.Syntax.SpanStart)
                return true;

            return !ReferenceEquals(resetBlock, targetBlock)
                && CanReachAfterLeavingAvoiding(operationBlock, targetBlock, resetBlock);
        }

        if (ReferenceEquals(resetBlock, targetBlock))
            return false;

        return ReferenceEquals(operationBlock, resetBlock)
            ? CanReachAfterLeavingAvoiding(operationBlock, targetBlock, resetBlock)
            : CanReachAvoiding(operationBlock, targetBlock, resetBlock);
    }

    private static bool IsPrimaryKeyProperty(
        MutationEvidence mutation,
        LostUpdateCompilationEvidence evidence,
        CancellationToken cancellationToken
    )
    {
        var property = mutation.Property;
        var entityType = mutation.Entity.EntityType;
        if (
            evidence.TryGetFluentPrimaryKeys(
                entityType,
                mutation.Entity.Context,
                cancellationToken,
                out var configuredKeys
            )
        )
        {
            return configuredKeys.Contains(property.OriginalDefinition);
        }

        var attributedKeys = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        var hasAttributedKeyDefinition = false;
        for (var current = entityType; current != null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (
                    attribute.AttributeClass?.ToDisplayString()
                    != "Microsoft.EntityFrameworkCore.PrimaryKeyAttribute"
                )
                {
                    continue;
                }

                hasAttributedKeyDefinition = true;
                foreach (var argument in attribute.ConstructorArguments)
                    CollectAttributedPrimaryKeys(argument, current, attributedKeys);
            }

            foreach (var candidate in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (
                    candidate
                        .GetAttributes()
                        .Any(attribute =>
                            attribute.AttributeClass?.ToDisplayString()
                            == "System.ComponentModel.DataAnnotations.KeyAttribute"
                        )
                )
                {
                    hasAttributedKeyDefinition = true;
                    attributedKeys.Add(candidate.OriginalDefinition);
                }
            }
        }

        if (hasAttributedKeyDefinition)
            return attributedKeys.Contains(property.OriginalDefinition);

        var rootEntityType = entityType;
        while (
            rootEntityType.BaseType != null
            && rootEntityType.BaseType.SpecialType != SpecialType.System_Object
        )
        {
            rootEntityType = rootEntityType.BaseType;
        }

        var conventionalKey = FindConventionalKey("Id");
        conventionalKey ??= FindConventionalKey(rootEntityType.Name + "Id");
        return conventionalKey != null
            && SymbolEqualityComparer.Default.Equals(
                property.OriginalDefinition,
                conventionalKey.OriginalDefinition
            );

        IPropertySymbol? FindConventionalKey(string propertyName)
        {
            return rootEntityType
                .GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    !candidate.IsStatic
                    && !candidate.IsIndexer
                    && string.Equals(
                        candidate.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !evidence.IsIgnoredProperty(
                        entityType,
                        candidate,
                        mutation.Entity.Context,
                        cancellationToken
                    )
                );
        }
    }

    private static void CollectAttributedPrimaryKeys(
        TypedConstant argument,
        INamedTypeSymbol entityType,
        HashSet<IPropertySymbol> attributedKeys
    )
    {
        if (argument.Kind == TypedConstantKind.Array)
        {
            foreach (var item in argument.Values)
                CollectAttributedPrimaryKeys(item, entityType, attributedKeys);
            return;
        }

        if (argument.Value is not string propertyName)
            return;

        for (var current = entityType; current != null; current = current.BaseType)
        {
            var key = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (key == null)
                continue;

            attributedKeys.Add(key.OriginalDefinition);
            return;
        }
    }

    private static bool IsPlainSelfAssignment(
        ISimpleAssignmentOperation assignment,
        IPropertyReferenceOperation target,
        EntitySource entity,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        IOperation value = assignment.Value;
        while (value is IParenthesizedOperation parenthesized)
            value = parenthesized.Operand;

        return value is IPropertyReferenceOperation propertyRead
            && SymbolEqualityComparer.Default.Equals(propertyRead.Property, target.Property)
            && TryResolveEntity(
                propertyRead.Instance,
                contexts,
                queries,
                entities,
                collector,
                flowGraph,
                out var readEntity
            )
            && ReferenceEquals(readEntity, entity);
    }

    private static void AddDirectMutation(
        IOperation targetOperation,
        IOperation mutationOperation,
        int position,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        OperationCollector collector,
        ControlFlowGraph? flowGraph,
        List<MutationEvidence> mutations
    )
    {
        if (
            targetOperation is IPropertyReferenceOperation target
            && TryResolveEntity(
                target.Instance,
                contexts,
                queries,
                entities,
                collector,
                flowGraph,
                out var entity
            )
            && LostUpdateOperationFacts.IsScalarProperty(target.Property)
        )
        {
            mutations.Add(
                new MutationEvidence(
                    entity,
                    target.Property,
                    target.Syntax.GetLocation(),
                    position,
                    mutationOperation
                )
            );
        }
    }

    private static bool TryResolveMaterialization(
        IOperation operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        out EntitySource entity
    )
    {
        return TryResolveMaterialization(
            operation,
            contexts,
            queries,
            new OperationCollector(),
            flowGraph: null,
            out entity
        );
    }

    private static bool TryResolveMaterialization(
        IOperation operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        OperationCollector collector,
        ControlFlowGraph? flowGraph,
        out EntitySource entity
    )
    {
        if (
            operation is ICoalesceOperation coalesce
            && IsDefinitelyNonReturningAlternative(coalesce.WhenNull)
        )
        {
            operation = LostUpdateOperationFacts.Unwrap(coalesce.Value);
        }
        if (TryGetSynchronousCompletionSource(operation, out var completionSource))
            operation = completionSource;

        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation is IInvocationOperation configuredAwait
            && LostUpdateOperationFacts.IsFrameworkConfigureAwait(configuredAwait.TargetMethod)
            && configuredAwait.Instance is { } configuredOperation
        )
        {
            operation = LostUpdateOperationFacts.Unwrap(configuredOperation);
        }
        if (
            operation is not IInvocationOperation invocation
            || !LostUpdateOperationFacts.IsSingleEntityTerminal(invocation.TargetMethod)
        )
        {
            entity = null!;
            return false;
        }

        if (
            invocation.TargetMethod.Name is "Find" or "FindAsync"
            && LostUpdateOperationFacts.IsDbContextType(invocation.TargetMethod.ContainingType)
            && invocation.TargetMethod.TypeArguments.Length == 1
            && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol contextEntityType
            && TryResolveContext(invocation.Instance, contexts, out var findContext)
        )
        {
            entity = new EntitySource(
                findContext,
                contextEntityType,
                isTracked: true,
                honorsContextTrackingBehavior: false,
                invocation.Syntax.SpanStart,
                invocation,
                TryGetContextAccessSymbol(invocation.Instance, out var findAccess)
                    ? findAccess
                    : findContext
            );
            return true;
        }

        IOperation? sourceOperation = invocation.Instance;
        if (sourceOperation == null && invocation.Arguments.Length > 0)
            sourceOperation = invocation.Arguments[0].Value;

        if (
            sourceOperation != null
            && TryResolveQuery(sourceOperation, contexts, queries, out var query)
            && IsStableDbSetOrigin(query, invocation, collector, contexts, flowGraph)
        )
        {
            var isSetFind =
                invocation.TargetMethod.Name is "Find" or "FindAsync"
                && LostUpdateOperationFacts.TryGetDbSetEntityType(
                    invocation.TargetMethod.ContainingType,
                    out _
                );
            entity = new EntitySource(
                query.Context,
                query.EntityType,
                isSetFind || query.IsTracked,
                honorsContextTrackingBehavior: !isSetFind && query.ExplicitTracking == null,
                invocation.Syntax.SpanStart,
                invocation,
                query.ContextAccess
            );
            return true;
        }

        entity = null!;
        return false;
    }

    private static bool TryGetSynchronousCompletionSource(
        IOperation operation,
        out IOperation source
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation
                is IPropertyReferenceOperation
                {
                    Property: { } resultProperty,
                    Instance: { } resultInstance,
                }
            && LostUpdateOperationFacts.IsFrameworkTaskResultProperty(resultProperty)
        )
        {
            source = LostUpdateOperationFacts.Unwrap(resultInstance);
            return true;
        }

        if (
            operation
                is IInvocationOperation
                {
                    TargetMethod: { } getResultMethod,
                    Instance: { } getResultInstance,
                }
            && LostUpdateOperationFacts.Unwrap(getResultInstance)
                is IInvocationOperation
                {
                    TargetMethod: { } getAwaiterMethod,
                    Instance: { } awaitable,
                }
            && LostUpdateOperationFacts.IsFrameworkBlockingGetResult(
                getAwaiterMethod,
                getResultMethod
            )
        )
        {
            source = LostUpdateOperationFacts.Unwrap(awaitable);
            return true;
        }

        source = null!;
        return false;
    }

    private static bool TryGetSynchronousCompletionOperation(
        IInvocationOperation invocation,
        out IOperation completion
    )
    {
        IOperation operation = invocation;
        while (
            operation.Parent is IConversionOperation conversion
                && ReferenceEquals(conversion.Operand, operation)
            || operation.Parent is IParenthesizedOperation parenthesized
                && ReferenceEquals(parenthesized.Operand, operation)
        )
        {
            operation = operation.Parent;
        }

        if (
            operation.Parent
                is IPropertyReferenceOperation
                {
                    Property: { } resultProperty,
                    Instance: { } resultInstance,
                } result
            && LostUpdateOperationFacts.IsFrameworkTaskResultProperty(resultProperty)
            && ReferenceEquals(LostUpdateOperationFacts.Unwrap(resultInstance), invocation)
        )
        {
            completion = result;
            return true;
        }

        if (
            operation.Parent is IInvocationOperation configuredAwait
            && LostUpdateOperationFacts.IsFrameworkConfigureAwait(configuredAwait.TargetMethod)
            && configuredAwait.Instance is { } configuredInstance
            && ReferenceEquals(LostUpdateOperationFacts.Unwrap(configuredInstance), invocation)
        )
        {
            operation = configuredAwait;
        }

        while (
            operation.Parent is IConversionOperation conversion
                && ReferenceEquals(conversion.Operand, operation)
            || operation.Parent is IParenthesizedOperation parenthesized
                && ReferenceEquals(parenthesized.Operand, operation)
        )
        {
            operation = operation.Parent;
        }

        if (
            operation.Parent
                is IInvocationOperation
                {
                    TargetMethod: { } getAwaiterMethod,
                    Instance: { } getAwaiterInstance,
                } getAwaiter
            && ReferenceEquals(LostUpdateOperationFacts.Unwrap(getAwaiterInstance), operation)
        )
        {
            operation = getAwaiter;
            while (
                operation.Parent is IConversionOperation conversion
                    && ReferenceEquals(conversion.Operand, operation)
                || operation.Parent is IParenthesizedOperation parenthesized
                    && ReferenceEquals(parenthesized.Operand, operation)
            )
            {
                operation = operation.Parent;
            }

            if (
                operation.Parent
                    is IInvocationOperation
                    {
                        TargetMethod: { } getResultMethod,
                        Instance: { } getResultInstance,
                    } getResult
                && ReferenceEquals(LostUpdateOperationFacts.Unwrap(getResultInstance), operation)
                && LostUpdateOperationFacts.IsFrameworkBlockingGetResult(
                    getAwaiterMethod,
                    getResultMethod
                )
            )
            {
                completion = getResult;
                return true;
            }
        }

        completion = null!;
        return false;
    }

    private static bool IsDefinitelyNonReturningAlternative(IOperation operation)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation is IThrowOperation
            || operation is IInvocationOperation invocation
                && invocation
                    .TargetMethod.GetAttributes()
                    .Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString()
                        == "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute"
                    );
    }

    private static bool TryResolveQuery(
        IOperation operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        out QuerySource source
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);

        if (
            operation is ILocalReferenceOperation local
            && queries.TryGetValue(local.Local, out source!)
        )
            return true;

        if (
            operation is IPropertyReferenceOperation property
            && LostUpdateOperationFacts.TryGetDbSetEntityType(property.Type, out var propertyEntity)
            && IsStableDbSetProperty(property.Property)
            && TryResolveContext(property.Instance, contexts, out var propertyContext)
        )
        {
            source = new QuerySource(
                propertyContext,
                propertyEntity,
                isTracked: true,
                dbSetOrigin: property,
                contextAccess: TryGetContextAccessSymbol(property.Instance, out var propertyAccess)
                    ? propertyAccess
                    : propertyContext
            );
            return true;
        }

        if (operation is IInvocationOperation invocation)
        {
            if (
                invocation.TargetMethod.Name == "Set"
                && invocation.TargetMethod.TypeArguments.Length == 1
                && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol setEntity
                && LostUpdateOperationFacts.IsDbContextType(invocation.TargetMethod.ContainingType)
                && !HasDefinitelyInvalidSetName(invocation)
                && TryResolveContext(invocation.Instance, contexts, out var setContext)
            )
            {
                source = new QuerySource(
                    setContext,
                    setEntity,
                    isTracked: true,
                    contextAccess: TryGetContextAccessSymbol(invocation.Instance, out var setAccess)
                        ? setAccess
                        : setContext
                );
                return true;
            }

            IOperation? inner = invocation.Instance;
            if (inner == null && invocation.Arguments.Length > 0)
                inner = invocation.Arguments[0].Value;

            if (
                inner != null
                && TryResolveQuery(inner, contexts, queries, out var innerSource)
                && LostUpdateOperationFacts.IsShapePreservingQueryMethod(invocation.TargetMethod)
            )
            {
                if (
                    invocation.TargetMethod.Name
                    is "AsNoTracking"
                        or "AsNoTrackingWithIdentityResolution"
                )
                    source = innerSource.WithTracking(false);
                else if (invocation.TargetMethod.Name == "AsTracking")
                    source = innerSource.WithTracking(
                        !TryGetQueryTrackingBehaviorArgument(invocation, out var behavior)
                            || behavior is not ("NoTracking" or "NoTrackingWithIdentityResolution")
                    );
                else
                    source = innerSource;

                return true;
            }
        }

        source = null!;
        return false;
    }

    private static bool TryGetQueryTrackingBehaviorArgument(
        IInvocationOperation invocation,
        out string behavior
    )
    {
        foreach (var argument in invocation.Arguments)
        {
            if (
                argument.Parameter?.Type.ToDisplayString()
                != "Microsoft.EntityFrameworkCore.QueryTrackingBehavior"
            )
            {
                continue;
            }

            if (
                LostUpdateOperationFacts.Unwrap(argument.Value) is IFieldReferenceOperation field
                && field.Field.ContainingType.ToDisplayString()
                    == "Microsoft.EntityFrameworkCore.QueryTrackingBehavior"
            )
            {
                behavior = field.Field.Name;
                return true;
            }

            break;
        }

        behavior = string.Empty;
        return false;
    }

    private static bool IsStableDbSetProperty(IPropertySymbol property)
    {
        if (
            property.IsStatic
            || property.IsVirtual
            || property.IsOverride
            || property.IsAbstract
            || !LostUpdateOperationFacts.IsDbContextType(property.ContainingType)
            || property.DeclaringSyntaxReferences.IsEmpty
            || HidesDbSetProperty(property)
        )
        {
            return false;
        }

        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (
                syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration
                || declaration.ExpressionBody != null
                || declaration.AccessorList?.Accessors.FirstOrDefault(accessor =>
                    accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration)
                )
                    is not { Body: null, ExpressionBody: null }
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool HidesDbSetProperty(IPropertySymbol property)
    {
        for (
            var current = property.ContainingType.BaseType;
            current != null;
            current = current.BaseType
        )
        {
            if (
                current
                    .GetMembers(property.Name)
                    .OfType<IPropertySymbol>()
                    .Any(candidate =>
                        LostUpdateOperationFacts.TryGetDbSetEntityType(candidate.Type, out _)
                    )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStableDbSetOrigin(
        QuerySource query,
        IInvocationOperation materialization,
        OperationCollector collector,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        ControlFlowGraph? flowGraph
    )
    {
        if (query.DbSetOrigin == null)
            return true;

        return !collector.SimpleAssignments.Any(assignment =>
                assignment.Syntax.SpanStart < materialization.Syntax.SpanStart
                && IsSameDbSetRoot(assignment.Target, query, contexts)
                && (flowGraph == null || OperationCanReach(assignment, materialization, flowGraph))
            )
            && !collector.Invocations.Any(invocation =>
                invocation.Syntax.SpanStart < materialization.Syntax.SpanStart
                && invocation.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && IsSameDbSetRoot(argument.Value, query, contexts)
                )
                && (flowGraph == null || OperationCanReach(invocation, materialization, flowGraph))
            );
    }

    private static bool IsSameDbSetRoot(
        IOperation operation,
        QuerySource query,
        Dictionary<ILocalSymbol, ISymbol> contexts
    )
    {
        return LostUpdateOperationFacts.Unwrap(operation) is IPropertyReferenceOperation property
            && SymbolEqualityComparer.Default.Equals(
                property.Property.OriginalDefinition,
                query.DbSetOrigin!.Property.OriginalDefinition
            )
            && TryResolveContext(property.Instance, contexts, out var context)
            && SymbolEqualityComparer.Default.Equals(context, query.Context);
    }

    private static bool HasDefinitelyInvalidSetName(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (
                argument.Parameter?.Type.SpecialType != SpecialType.System_String
                || argument.Value.ConstantValue is not { HasValue: true } constant
            )
            {
                continue;
            }

            return constant.Value == null
                || constant.Value is string name && string.IsNullOrWhiteSpace(name);
        }

        return false;
    }

    private static bool TryResolveContext(
        IOperation? operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out ISymbol contextSymbol
    )
    {
        if (operation == null)
        {
            contextSymbol = null!;
            return false;
        }

        operation = LostUpdateOperationFacts.Unwrap(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local
                when contexts.TryGetValue(local.Local, out contextSymbol!):
                return true;
            case IParameterReferenceOperation parameter
                when LostUpdateOperationFacts.IsDbContextType(parameter.Parameter.Type):
                contextSymbol = parameter.Parameter;
                return true;
            case IFieldReferenceOperation field
                when LostUpdateOperationFacts.IsDbContextType(field.Field.Type)
                    && (
                        field.Field.IsStatic
                        || field.Instance != null
                            && LostUpdateOperationFacts.Unwrap(field.Instance)
                                is IInstanceReferenceOperation
                    ):
                contextSymbol = field.Field;
                return true;
            case IInstanceReferenceOperation instance
                when LostUpdateOperationFacts.IsDbContextType(instance.Type):
                contextSymbol = instance.Type!;
                return true;
            default:
                contextSymbol = null!;
                return false;
        }
    }

    private static bool TryGetContextAccessSymbol(IOperation? operation, out ISymbol symbol)
    {
        if (operation != null)
        {
            switch (LostUpdateOperationFacts.Unwrap(operation))
            {
                case ILocalReferenceOperation local:
                    symbol = local.Local;
                    return true;
                case IParameterReferenceOperation parameter:
                    symbol = parameter.Parameter;
                    return true;
                case IFieldReferenceOperation field:
                    symbol = field.Field;
                    return true;
                case IInstanceReferenceOperation instance when instance.Type != null:
                    symbol = instance.Type;
                    return true;
            }
        }

        symbol = null!;
        return false;
    }

    private static bool IsObservedTransactionOperation(IInvocationOperation invocation)
    {
        return !invocation.TargetMethod.Name.EndsWith("Async", StringComparison.Ordinal)
            || LostUpdateCompilationEvidence.IsCompletionObserved(invocation)
            || TryGetSynchronousCompletionOperation(invocation, out _);
    }

    private static bool IsImmediatelyTerminatedTransaction(IInvocationOperation invocation)
    {
        IOperation operation = invocation;
        if (TryGetSynchronousCompletionOperation(invocation, out var completion))
            operation = completion;
        while (operation.Parent != null)
        {
            switch (operation.Parent)
            {
                case IConversionOperation conversion
                    when ReferenceEquals(conversion.Operand, operation):
                case IParenthesizedOperation parenthesized
                    when ReferenceEquals(parenthesized.Operand, operation):
                case IAwaitOperation awaitOperation
                    when ContainsOperation(awaitOperation.Operation, operation):
                    operation = operation.Parent;
                    continue;
                case IInvocationOperation candidate
                    when candidate.Instance != null
                        && ContainsOperation(candidate.Instance, operation):
                    if (
                        LostUpdateOperationFacts.IsTransactionTerminationMethod(
                            candidate.TargetMethod
                        )
                    )
                    {
                        return true;
                    }

                    if (LostUpdateOperationFacts.IsFrameworkConfigureAwait(candidate.TargetMethod))
                    {
                        operation = candidate;
                        continue;
                    }

                    return false;
                default:
                    return false;
            }
        }

        return false;
    }

    private static TransactionArgumentNullState GetUseTransactionArgumentNullState(
        IInvocationOperation invocation,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        var transactionArgument = invocation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Type.Name.EndsWith("Transaction", StringComparison.Ordinal) == true
        );
        if (transactionArgument == null || flowGraph == null)
            return TransactionArgumentNullState.Unknown;

        return GetTransactionValueNullState(
            transactionArgument.Value,
            invocation,
            collector,
            flowGraph,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default)
        );
    }

    private static TransactionArgumentNullState GetTransactionValueNullState(
        IOperation operation,
        IInvocationOperation useTransaction,
        OperationCollector collector,
        ControlFlowGraph flowGraph,
        HashSet<ISymbol> visited
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (operation.ConstantValue is { HasValue: true, Value: null })
            return TransactionArgumentNullState.Null;

        if (operation is IObjectCreationOperation)
            return TransactionArgumentNullState.NonNull;

        if (operation is ICoalesceOperation { Value: var coalesceValue, WhenNull: var whenNull })
        {
            var valueState = GetTransactionValueNullState(
                coalesceValue,
                useTransaction,
                collector,
                flowGraph,
                visited
            );
            var fallbackState = GetTransactionValueNullState(
                whenNull,
                useTransaction,
                collector,
                flowGraph,
                visited
            );
            return
                valueState == TransactionArgumentNullState.NonNull
                || fallbackState == TransactionArgumentNullState.NonNull
                ? TransactionArgumentNullState.NonNull
                : TransactionArgumentNullState.Unknown;
        }

        if (operation is IConditionalOperation conditional)
        {
            if (conditional.WhenFalse == null)
                return TransactionArgumentNullState.Unknown;
            var whenTrue = GetTransactionValueNullState(
                conditional.WhenTrue,
                useTransaction,
                collector,
                flowGraph,
                visited
            );
            var whenFalse = GetTransactionValueNullState(
                conditional.WhenFalse,
                useTransaction,
                collector,
                flowGraph,
                visited
            );
            return whenTrue == whenFalse ? whenTrue : TransactionArgumentNullState.Unknown;
        }

        ISymbol? referencedSymbol = operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };
        if (referencedSymbol != null)
        {
            var pathState = GetTransactionNullStateRequiredOnPath(
                referencedSymbol,
                useTransaction,
                flowGraph
            );
            if (pathState != TransactionArgumentNullState.Unknown)
                return pathState;

            if (!visited.Add(referencedSymbol))
                return TransactionArgumentNullState.Unknown;

            if (
                referencedSymbol is ILocalSymbol
                && TryGetDominatingTransactionValue(
                    referencedSymbol,
                    useTransaction,
                    collector,
                    flowGraph,
                    out var value
                )
            )
            {
                return GetTransactionValueNullState(
                    value,
                    useTransaction,
                    collector,
                    flowGraph,
                    visited
                );
            }

            var nullableAnnotation = referencedSymbol switch
            {
                ILocalSymbol local => local.NullableAnnotation,
                IParameterSymbol parameter => parameter.NullableAnnotation,
                _ => NullableAnnotation.None,
            };
            return nullableAnnotation == NullableAnnotation.NotAnnotated
                ? TransactionArgumentNullState.NonNull
                : TransactionArgumentNullState.Unknown;
        }

        if (
            operation is IInvocationOperation valueInvocation
            && (
                valueInvocation.TargetMethod.Name is "BeginTransaction" or "BeginTransactionAsync"
                || valueInvocation.Type?.NullableAnnotation == NullableAnnotation.NotAnnotated
            )
        )
        {
            return TransactionArgumentNullState.NonNull;
        }

        return operation.Type?.NullableAnnotation == NullableAnnotation.NotAnnotated
            ? TransactionArgumentNullState.NonNull
            : TransactionArgumentNullState.Unknown;
    }

    private static bool TryGetDominatingTransactionValue(
        ISymbol symbol,
        IInvocationOperation useTransaction,
        OperationCollector collector,
        ControlFlowGraph flowGraph,
        out IOperation value
    )
    {
        value = null!;
        var valuePosition = -1;
        foreach (var declarator in collector.Declarators)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(declarator.Symbol, symbol)
                || declarator.Initializer?.Value is not { } initializer
                || initializer.Syntax.SpanStart >= useTransaction.Syntax.SpanStart
                || !OperationDominates(initializer, useTransaction, flowGraph)
            )
            {
                continue;
            }

            value = initializer;
            valuePosition = initializer.Syntax.SpanStart;
        }

        foreach (var assignment in collector.SimpleAssignments)
        {
            if (
                assignment.Syntax.SpanStart <= valuePosition
                || assignment.Syntax.SpanStart >= useTransaction.Syntax.SpanStart
                || !IsSymbolReference(assignment.Target, symbol)
                || !OperationDominates(assignment, useTransaction, flowGraph)
            )
            {
                continue;
            }

            value = assignment.Value;
            valuePosition = assignment.Syntax.SpanStart;
        }

        if (valuePosition < 0)
            return false;

        return !collector.SimpleAssignments.Any(assignment =>
            assignment.Syntax.SpanStart > valuePosition
            && assignment.Syntax.SpanStart < useTransaction.Syntax.SpanStart
            && IsSymbolReference(assignment.Target, symbol)
            && OperationCanReach(assignment, useTransaction, flowGraph)
        );
    }

    private static TransactionArgumentNullState GetTransactionNullStateRequiredOnPath(
        ISymbol symbol,
        IInvocationOperation useTransaction,
        ControlFlowGraph flowGraph
    )
    {
        var target = FindContainingBlock(flowGraph, useTransaction);
        if (target == null)
            return TransactionArgumentNullState.Unknown;

        var reachableWhenNull = CanReachWithNullState(
            flowGraph.Blocks[0],
            target,
            symbol,
            assumedNull: true
        );
        var reachableWhenNonNull = CanReachWithNullState(
            flowGraph.Blocks[0],
            target,
            symbol,
            assumedNull: false
        );
        if (!reachableWhenNull && reachableWhenNonNull)
            return TransactionArgumentNullState.NonNull;
        if (reachableWhenNull && !reachableWhenNonNull)
            return TransactionArgumentNullState.Null;
        return TransactionArgumentNullState.Unknown;
    }

    private static bool TryResolveTransactionContext(
        IInvocationOperation invocation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out ISymbol contextSymbol
    )
    {
        if (
            invocation.Instance != null
            && TryResolveContextWithin(invocation.Instance, contexts, out contextSymbol)
        )
        {
            return true;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (TryResolveContextWithin(argument.Value, contexts, out contextSymbol))
                return true;
        }

        contextSymbol = null!;
        return false;
    }

    private static bool TryResolveContextWithin(
        IOperation operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out ISymbol contextSymbol
    )
    {
        if (TryResolveContext(operation, contexts, out contextSymbol))
            return true;

        foreach (var child in operation.ChildOperations)
        {
            if (TryResolveContextWithin(child, contexts, out contextSymbol))
                return true;
        }

        contextSymbol = null!;
        return false;
    }

    private static bool TryResolveEntity(
        IOperation? operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        OperationCollector collector,
        ControlFlowGraph? flowGraph,
        out EntitySource entity
    )
    {
        if (TryResolveEntity(operation, entities, out entity))
            return true;

        return operation != null
            && TryResolveMaterialization(
                operation,
                contexts,
                queries,
                collector,
                flowGraph,
                out entity
            );
    }

    private static bool TryResolveEntity(
        IOperation? operation,
        Dictionary<ILocalSymbol, EntitySource> entities,
        out EntitySource entity
    )
    {
        if (
            operation != null
            && TryGetSynchronousCompletionSource(operation, out var completionSource)
        )
        {
            return TryResolveEntity(completionSource, entities, out entity);
        }

        if (
            operation != null
            && LostUpdateOperationFacts.Unwrap(operation) is ILocalReferenceOperation local
            && entities.TryGetValue(local.Local, out entity!)
        )
        {
            return true;
        }

        entity = null!;
        return false;
    }

    private static bool TryResolveEntity(
        IOperation? operation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        out EntitySource entity
    )
    {
        if (TryResolveEntity(operation, entities, out entity))
            return true;

        return operation != null
            && TryResolveMaterialization(operation, contexts, queries, out entity);
    }

    private static bool ContainsEntity(
        IOperation operation,
        EntitySource expected,
        Dictionary<ILocalSymbol, EntitySource> entities
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            TryResolveEntity(operation, entities, out var entity)
            && ReferenceEquals(entity, expected)
        )
        {
            return true;
        }

        if (operation is IArrayCreationOperation { Initializer: { } initializer })
            return initializer.ElementValues.Any(item => ContainsEntity(item, expected, entities));

        if (operation is IArrayInitializerOperation arrayInitializer)
        {
            return arrayInitializer.ElementValues.Any(item =>
                ContainsEntity(item, expected, entities)
            );
        }

        return false;
    }

    private static bool ContainsEntityPropertyRead(
        IOperation operation,
        IPropertySymbol property,
        EntitySource entity,
        Dictionary<ILocalSymbol, EntitySource> entities
    )
    {
        var collector = new OperationCollector();
        collector.Visit(operation);
        foreach (var propertyReference in collector.PropertyReferences)
        {
            if (
                !LostUpdateOperationFacts.IsInsideNameOf(propertyReference)
                && !IsInCompileTimeDeadBranch(propertyReference, operation)
                && SymbolEqualityComparer.Default.Equals(propertyReference.Property, property)
                && TryResolveEntity(propertyReference.Instance, entities, out var readEntity)
                && ReferenceEquals(readEntity, entity)
            )
            {
                return true;
            }
        }

        return false;
    }

    private sealed class LoadedValueAnalysis
    {
        private readonly OperationCollector _collector;
        private readonly HashSet<ILocalSymbol> _unstableLocals;
        private readonly Dictionary<ILocalSymbol, IVariableDeclaratorOperation> _declarations;
        private readonly Dictionary<ILocalSymbol, ISymbol> _contexts;
        private readonly Dictionary<ILocalSymbol, EntitySource> _entities;
        private readonly ControlFlowGraph? _flowGraph;

        internal LoadedValueAnalysis(
            OperationCollector collector,
            HashSet<ILocalSymbol> unstableLocals,
            Dictionary<ILocalSymbol, ISymbol> contexts,
            Dictionary<ILocalSymbol, EntitySource> entities,
            ControlFlowGraph? flowGraph
        )
        {
            _collector = collector;
            _unstableLocals = unstableLocals;
            _contexts = contexts;
            _entities = entities;
            _flowGraph = flowGraph;
            _declarations = new Dictionary<ILocalSymbol, IVariableDeclaratorOperation>(
                SymbolEqualityComparer.Default
            );

            foreach (var declarator in collector.Declarators)
            {
                if (declarator.Initializer?.Value == null)
                    continue;

                _declarations[declarator.Symbol] = declarator;
            }
        }

        internal bool Contains(IOperation operation, IPropertySymbol property, EntitySource entity)
        {
            return ContainsEntityPropertyRead(operation, property, entity, _entities)
                || ContainsCaptured(operation, property, entity);
        }

        internal bool ContainsCaptured(
            IOperation operation,
            IPropertySymbol property,
            EntitySource entity
        )
        {
            var collector = new OperationCollector();
            collector.Visit(operation);
            foreach (var localReference in collector.LocalReferences)
            {
                if (
                    IsInCompileTimeDeadBranch(localReference, operation)
                    || !DependsOnLoadedValue(
                        localReference.Local,
                        property,
                        entity,
                        localReference,
                        new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
                    )
                )
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool DependsOnLoadedValue(
            ILocalSymbol local,
            IPropertySymbol property,
            EntitySource entity,
            ILocalReferenceOperation use,
            HashSet<ILocalSymbol> visiting
        )
        {
            if (
                _unstableLocals.Contains(local)
                || !visiting.Add(local)
                || !_declarations.TryGetValue(local, out var declaration)
                || declaration.Initializer?.Value is not { } initializer
                || declaration.Syntax.SpanStart >= use.Syntax.SpanStart
            )
            {
                return false;
            }

            var result = DependsOnLoadedValue(initializer, property, entity, use, visiting);
            visiting.Remove(local);
            return result;
        }

        private bool DependsOnLoadedValue(
            IOperation operation,
            IPropertySymbol property,
            EntitySource entity,
            ILocalReferenceOperation use,
            HashSet<ILocalSymbol> visiting
        )
        {
            operation = LostUpdateOperationFacts.Unwrap(operation);
            switch (operation)
            {
                case IPropertyReferenceOperation propertyReference:
                    return !LostUpdateOperationFacts.IsInsideNameOf(propertyReference)
                        && SymbolEqualityComparer.Default.Equals(
                            propertyReference.Property,
                            property
                        )
                        && TryResolveEntity(
                            propertyReference.Instance,
                            _entities,
                            out var readEntity
                        )
                        && ReferenceEquals(readEntity, entity)
                        && IsSourceValidAtUse(propertyReference, use, property, entity);

                case ILocalReferenceOperation alias:
                    return DependsOnLoadedValue(alias.Local, property, entity, use, visiting);

                case IConversionOperation { OperatorMethod: null } conversion:
                    return DependsOnLoadedValue(
                        conversion.Operand,
                        property,
                        entity,
                        use,
                        visiting
                    );

                case IUnaryOperation { OperatorMethod: null } unary:
                    return DependsOnLoadedValue(unary.Operand, property, entity, use, visiting);

                case IBinaryOperation { OperatorMethod: null } binary:
                    return DependsOnBinaryValue(binary, property, entity, use, visiting);

                case IConditionalOperation conditional:
                    if (
                        DependsOnLoadedValue(conditional.Condition, property, entity, use, visiting)
                    )
                    {
                        return true;
                    }

                    if (
                        conditional.Condition.ConstantValue is
                        { HasValue: true, Value: bool condition }
                    )
                    {
                        var selected = condition ? conditional.WhenTrue : conditional.WhenFalse;
                        return selected != null
                            && DependsOnLoadedValue(selected, property, entity, use, visiting);
                    }

                    return conditional.WhenTrue is { } whenTrue
                        && conditional.WhenFalse is { } whenFalse
                        && DependsOnLoadedValue(whenTrue, property, entity, use, visiting)
                        && DependsOnLoadedValue(whenFalse, property, entity, use, visiting);

                case ICoalesceOperation coalesce:
                    return DependsOnLoadedValue(coalesce.Value, property, entity, use, visiting);

                default:
                    return false;
            }
        }

        private bool DependsOnBinaryValue(
            IBinaryOperation binary,
            IPropertySymbol property,
            EntitySource entity,
            ILocalReferenceOperation use,
            HashSet<ILocalSymbol> visiting
        )
        {
            if (DependsOnLoadedValue(binary.LeftOperand, property, entity, use, visiting))
            {
                return true;
            }

            if (
                binary.OperatorKind
                is BinaryOperatorKind.ConditionalAnd
                    or BinaryOperatorKind.ConditionalOr
            )
            {
                if (
                    binary.LeftOperand.ConstantValue
                    is not { HasValue: true, Value: bool leftValue }
                )
                {
                    return false;
                }

                if (
                    binary.OperatorKind == BinaryOperatorKind.ConditionalAnd && !leftValue
                    || binary.OperatorKind == BinaryOperatorKind.ConditionalOr && leftValue
                )
                {
                    return false;
                }
            }

            return DependsOnLoadedValue(binary.RightOperand, property, entity, use, visiting);
        }

        private bool IsSourceValidAtUse(
            IPropertyReferenceOperation source,
            ILocalReferenceOperation use,
            IPropertySymbol property,
            EntitySource entity
        )
        {
            return source.Syntax.SpanStart < use.Syntax.SpanStart
                && !HasPropertyResetBeforeSource(source, property, entity);
        }

        private bool HasPropertyResetBeforeSource(
            IPropertyReferenceOperation source,
            IPropertySymbol property,
            EntitySource entity
        )
        {
            if (
                _collector.Invocations.Any(invocation =>
                    IsMatchingCompletedReload(invocation, entity, _contexts, _entities)
                    && IsStrictlyBetween(entity.Materialization, invocation, source)
                )
            )
            {
                return true;
            }

            return _collector.SimpleAssignments.Any(assignment =>
                IsBlindReset(assignment, property, entity)
                && AssignmentCompletesOnEveryPathToDestination(assignment, property, source)
                && IsStrictlyBetween(entity.Materialization, assignment, source)
            );
        }

        private bool IsBlindReset(
            ISimpleAssignmentOperation assignment,
            IPropertySymbol property,
            EntitySource entity
        )
        {
            return assignment.Target is IPropertyReferenceOperation target
                && SymbolEqualityComparer.Default.Equals(target.Property, property)
                && TryResolveEntity(target.Instance, _entities, out var assignedEntity)
                && ReferenceEquals(assignedEntity, entity)
                && !ContainsEntityPropertyRead(assignment.Value, property, entity, _entities)
                && !IsGuardedByEntityPropertyRead(
                    assignment,
                    property,
                    entity,
                    _entities,
                    _flowGraph
                );
        }

        private bool IsStrictlyBetween(IOperation start, IOperation middle, IOperation end)
        {
            return middle.Syntax.SpanStart > start.Syntax.SpanStart
                && middle.Syntax.SpanStart < end.Syntax.SpanStart
                && _flowGraph != null
                && OperationCanReach(start, middle, _flowGraph)
                && OperationDominates(middle, end, _flowGraph);
        }

        private bool CanFlowBetween(IOperation start, IOperation end)
        {
            return _flowGraph == null || OperationCanReach(start, end, _flowGraph);
        }
    }

    private static bool IsInCompileTimeDeadBranch(IOperation candidate, IOperation root)
    {
        for (
            var current = candidate;
            current != null && !ReferenceEquals(current, root);
            current = current.Parent
        )
        {
            if (
                current.Parent is IConditionalOperation conditional
                && conditional.Condition.ConstantValue is { HasValue: true, Value: bool condition }
                && (
                    ReferenceEquals(current, conditional.WhenTrue) && !condition
                    || ReferenceEquals(current, conditional.WhenFalse) && condition
                )
            )
            {
                return true;
            }

            if (
                current.Parent is IBinaryOperation binary
                && ReferenceEquals(current, binary.RightOperand)
                && binary.LeftOperand.ConstantValue is { HasValue: true, Value: bool leftValue }
                && (
                    binary.OperatorKind == BinaryOperatorKind.ConditionalAnd && !leftValue
                    || binary.OperatorKind == BinaryOperatorKind.ConditionalOr && leftValue
                )
            )
            {
                return true;
            }

            if (
                current.Parent is ICoalesceOperation coalesce
                && ReferenceEquals(current, coalesce.WhenNull)
                && coalesce.Value.ConstantValue is { HasValue: true, Value: not null }
            )
            {
                return true;
            }

            if (
                current is ISwitchExpressionArmOperation arm
                && current.Parent is ISwitchExpressionOperation switchExpression
                && TryGetSelectedSwitchArm(switchExpression, out var selectedArm)
                && !ReferenceEquals(arm, selectedArm)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSelectedSwitchArm(
        ISwitchExpressionOperation switchExpression,
        out ISwitchExpressionArmOperation selectedArm
    )
    {
        var value = switchExpression.Value.ConstantValue;
        if (!value.HasValue)
        {
            selectedArm = null!;
            return false;
        }

        foreach (var arm in switchExpression.Arms)
        {
            if (arm.Guard?.ConstantValue is { HasValue: true, Value: false })
                continue;

            if (
                arm.Pattern is IDiscardPatternOperation
                || arm.Pattern is IConstantPatternOperation constantPattern
                    && constantPattern.Value.ConstantValue is { HasValue: true } patternValue
                    && Equals(patternValue.Value, value.Value)
            )
            {
                if (
                    arm.Guard != null
                    && arm.Guard.ConstantValue is not { HasValue: true, Value: true }
                )
                {
                    selectedArm = null!;
                    return false;
                }

                selectedArm = arm;
                return true;
            }

            if (arm.Pattern is not IConstantPatternOperation)
            {
                selectedArm = null!;
                return false;
            }
        }

        selectedArm = null!;
        return false;
    }

    private static bool IsGuardedByEntityPropertyRead(
        IOperation mutation,
        IPropertySymbol property,
        EntitySource entity,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph,
        LoadedValueAnalysis? loadedValues = null
    )
    {
        for (var current = mutation.Parent; current != null; current = current.Parent)
        {
            if (
                current is IConditionalOperation conditional
                && !IsDescendantOf(mutation, conditional.Condition)
                && ContainsGuardPropertyRead(
                    conditional.Condition,
                    property,
                    entity,
                    entities,
                    loadedValues
                )
            )
            {
                return true;
            }

            if (
                current is ISwitchOperation switchOperation
                && !IsDescendantOf(mutation, switchOperation.Value)
                && ContainsGuardPropertyRead(
                    switchOperation.Value,
                    property,
                    entity,
                    entities,
                    loadedValues
                )
            )
            {
                return true;
            }

            if (
                current is IWhileLoopOperation whileLoop
                && whileLoop.ConditionIsTop
                && whileLoop.Condition != null
                && !IsDescendantOf(mutation, whileLoop.Condition)
                && ContainsGuardPropertyRead(
                    whileLoop.Condition,
                    property,
                    entity,
                    entities,
                    loadedValues
                )
            )
            {
                return true;
            }

            if (
                current is IForLoopOperation forLoop
                && forLoop.Condition != null
                && !IsDescendantOf(mutation, forLoop.Condition)
                && ContainsGuardPropertyRead(
                    forLoop.Condition,
                    property,
                    entity,
                    entities,
                    loadedValues
                )
            )
            {
                return true;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        return flowGraph != null
            && IsEstablishedByPrecedingFlowPredicate(
                mutation,
                property,
                entity,
                entities,
                flowGraph,
                loadedValues
            );
    }

    private static bool ContainsGuardPropertyRead(
        IOperation condition,
        IPropertySymbol property,
        EntitySource entity,
        Dictionary<ILocalSymbol, EntitySource> entities,
        LoadedValueAnalysis? loadedValues
    )
    {
        return loadedValues?.Contains(condition, property, entity)
            ?? ContainsEntityPropertyRead(condition, property, entity, entities);
    }

    private static bool IsEstablishedByPrecedingFlowPredicate(
        IOperation mutation,
        IPropertySymbol property,
        EntitySource entity,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph flowGraph,
        LoadedValueAnalysis? loadedValues
    )
    {
        var mutationBlock = FindContainingBlock(flowGraph, mutation);
        if (mutationBlock == null)
            return false;

        foreach (var predicateBlock in flowGraph.Blocks)
        {
            if (
                predicateBlock.BranchValue == null
                || predicateBlock.ConditionKind == ControlFlowConditionKind.None
                || ReferenceEquals(predicateBlock, mutationBlock)
                || !ContainsGuardPropertyRead(
                    predicateBlock.BranchValue,
                    property,
                    entity,
                    entities,
                    loadedValues
                )
                || !CanReach(flowGraph.Blocks[0], predicateBlock)
                || CanReachAvoiding(flowGraph.Blocks[0], mutationBlock, predicateBlock)
            )
            {
                continue;
            }

            var fallThrough = predicateBlock.FallThroughSuccessor?.Destination;
            var conditional = predicateBlock.ConditionalSuccessor?.Destination;
            var fallThroughReaches =
                fallThrough != null && CanReachAvoiding(fallThrough, mutationBlock, predicateBlock);
            var conditionalReaches =
                conditional != null && CanReachAvoiding(conditional, mutationBlock, predicateBlock);
            if (fallThroughReaches != conditionalReaches)
                return true;
        }

        return false;
    }

    private static bool IsDescendantOf(IOperation operation, IOperation ancestor)
    {
        for (IOperation? current = operation; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static bool TryGetArgument(
        IInvocationOperation invocation,
        int parameterOrdinal,
        out IArgumentOperation argument
    )
    {
        foreach (var candidate in invocation.Arguments)
        {
            if (candidate.Parameter?.Ordinal == parameterOrdinal)
            {
                argument = candidate;
                return true;
            }
        }

        argument = null!;
        return false;
    }

    private static bool TryResolveHelperContext(
        HelperTarget target,
        IInvocationOperation invocation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out ISymbol context
    )
    {
        if (
            target.ParameterOrdinal is int parameterOrdinal
            && TryGetArgument(invocation, parameterOrdinal, out var argument)
        )
        {
            return TryResolveContext(argument.Value, contexts, out context);
        }

        switch (target.Symbol)
        {
            case ILocalSymbol local when contexts.TryGetValue(local, out context!):
                return true;
            case IParameterSymbol parameter
                when LostUpdateOperationFacts.IsDbContextType(parameter.Type):
                context = parameter;
                return true;
            case IFieldSymbol field
                when field.IsReadOnly && LostUpdateOperationFacts.IsDbContextType(field.Type):
                context = field;
                return true;
            default:
                context = null!;
                return false;
        }
    }

    private static bool TryGetHelperContextAccessSymbol(
        HelperTarget target,
        IInvocationOperation invocation,
        out ISymbol symbol
    )
    {
        if (
            target.ParameterOrdinal is int parameterOrdinal
            && TryGetArgument(invocation, parameterOrdinal, out var argument)
        )
        {
            return TryGetContextAccessSymbol(argument.Value, out symbol);
        }

        symbol = target.Symbol;
        return symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol;
    }

    private static bool TryResolveHelperEntity(
        HelperTarget target,
        IInvocationOperation invocation,
        Dictionary<ILocalSymbol, EntitySource> entities,
        out EntitySource entity
    )
    {
        if (
            target.ParameterOrdinal is int parameterOrdinal
            && TryGetArgument(invocation, parameterOrdinal, out var argument)
        )
        {
            return TryResolveEntity(argument.Value, entities, out entity);
        }

        if (target.Symbol is ILocalSymbol local && entities.TryGetValue(local, out entity!))
            return true;

        entity = null!;
        return false;
    }

    private static bool TryCreateHelperTransactionEvidence(
        IInvocationOperation invocation,
        HelperTransaction helperTransaction,
        ISymbol context,
        out TransactionEvidence transaction
    )
    {
        IOperation? condition = null;
        if (helperTransaction.ConditionParameterOrdinal.HasValue)
        {
            if (
                !TryGetArgument(
                    invocation,
                    helperTransaction.ConditionParameterOrdinal.Value,
                    out var conditionArgument
                )
            )
            {
                transaction = default;
                return false;
            }

            if (
                conditionArgument.Value.ConstantValue is
                { HasValue: true, Value: bool conditionValue }
            )
            {
                if (conditionValue != helperTransaction.ConditionValue)
                {
                    transaction = default;
                    return false;
                }
            }
            else
            {
                condition = conditionArgument.Value;
            }
        }

        transaction = new TransactionEvidence(
            context,
            invocation.Syntax.SpanStart,
            invocation,
            helperTransaction.ProtectedSavePositions,
            condition,
            helperTransaction.ConditionValue
        );
        return true;
    }

    private static bool IsHelperSaveExecuted(IInvocationOperation invocation, HelperSave helperSave)
    {
        if (!helperSave.ConditionParameterOrdinal.HasValue)
            return true;

        return TryGetArgument(
                invocation,
                helperSave.ConditionParameterOrdinal.Value,
                out var conditionArgument
            )
            && conditionArgument.Value.ConstantValue
                is { HasValue: true, Value: bool conditionValue }
            && conditionValue == helperSave.ConditionValue;
    }

    private static bool IsHelperMutationPossible(
        IInvocationOperation invocation,
        HelperMutation helperMutation
    )
    {
        if (!helperMutation.ConditionParameterOrdinal.HasValue)
            return true;

        return !TryGetArgument(
                invocation,
                helperMutation.ConditionParameterOrdinal.Value,
                out var conditionArgument
            )
            || conditionArgument.Value.ConstantValue
                is not { HasValue: true, Value: bool conditionValue }
            || conditionValue == helperMutation.ConditionValue;
    }

    private static bool HasReattachmentBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        foreach (var invocation in invocations)
        {
            if (
                invocation.Syntax.SpanStart <= mutation.Entity.MaterializationPosition
                || invocation.Syntax.SpanStart >= save.Position
                || !LostUpdateOperationFacts.IsTrackingOperation(invocation.TargetMethod)
            )
            {
                continue;
            }

            ISymbol trackingContext;
            if (TryResolveContext(invocation.Instance, contexts, out trackingContext))
            {
                // Direct DbContext tracking call.
            }
            else if (
                invocation.Instance != null
                && TryResolveQuery(invocation.Instance, contexts, queries, out var trackingSet)
            )
            {
                trackingContext = trackingSet.Context;
            }
            else
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(trackingContext, mutation.Entity.Context))
            {
                continue;
            }

            foreach (var argument in invocation.Arguments)
            {
                if (
                    ContainsEntity(argument.Value, mutation.Entity, entities)
                    && TrackingSharesPath(
                        mutation,
                        save,
                        invocation,
                        LostUpdateOperationFacts.PersistsPriorMutation(invocation.TargetMethod),
                        flowGraph
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasExplicitSetterPersistenceBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart <= mutation.Position
                || assignment.Syntax.SpanStart >= save.Position
            )
            {
                continue;
            }

            var explicitlyModified =
                TryGetEntryStateAssignment(
                    assignment,
                    mutation,
                    contexts,
                    entities,
                    out var stateName
                )
                    && stateName == "Modified"
                || TryGetIsModifiedAssignment(assignment, mutation, contexts, entities);
            if (
                explicitlyModified
                && TrackingSharesPath(
                    mutation,
                    save,
                    assignment,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        foreach (var invocation in invocations)
        {
            if (
                invocation.Syntax.SpanStart <= mutation.Position
                || invocation.Syntax.SpanStart >= save.Position
                || !LostUpdateOperationFacts.PersistsPriorMutation(invocation.TargetMethod)
                || !TryResolveInvocationContext(
                    invocation,
                    contexts,
                    queries,
                    out var updateContext
                )
                || !SymbolEqualityComparer.Default.Equals(updateContext, mutation.Entity.Context)
                || !invocation.Arguments.Any(argument =>
                    ContainsEntity(argument.Value, mutation.Entity, entities)
                )
            )
            {
                continue;
            }

            if (
                TrackingSharesPath(
                    mutation,
                    save,
                    invocation,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEntryStatePersistenceBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart <= mutation.Entity.MaterializationPosition
                || assignment.Syntax.SpanStart >= save.Position
                || (
                    !TryGetEntryStateAssignment(
                        assignment,
                        mutation,
                        contexts,
                        entities,
                        out var stateName
                    )
                    || stateName != "Modified"
                        && (
                            stateName != "Unchanged"
                            || assignment.Syntax.SpanStart >= mutation.Position
                            || mutation.Entity.IsTracked
                        )
                ) && !TryGetIsModifiedAssignment(assignment, mutation, contexts, entities)
                || !TrackingSharesPath(
                    mutation,
                    save,
                    assignment,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetIsModifiedAssignment(
        ISimpleAssignmentOperation assignment,
        MutationEvidence mutation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities,
        bool expectedValue = true
    )
    {
        if (
            assignment.Target
                is not IPropertyReferenceOperation
                {
                    Property: { } isModifiedProperty,
                    Instance: { } modifiedReceiver,
                }
            || !LostUpdateOperationFacts.IsEfPropertyEntryIsModifiedProperty(isModifiedProperty)
            || assignment.Value.ConstantValue is not { HasValue: true, Value: bool assignedValue }
            || assignedValue != expectedValue
        )
        {
            return false;
        }

        var propertyInvocation =
            LostUpdateOperationFacts.Unwrap(modifiedReceiver) as IInvocationOperation;
        var entryInvocation = propertyInvocation?.Instance is { } propertyReceiver
            ? LostUpdateOperationFacts.Unwrap(propertyReceiver) as IInvocationOperation
            : null;
        return propertyInvocation != null
            && LostUpdateOperationFacts.IsEfEntityEntryPropertyMethod(
                propertyInvocation.TargetMethod
            )
            && entryInvocation != null
            && LostUpdateOperationFacts.IsEfDbContextEntryMethod(entryInvocation.TargetMethod)
            && TryResolveContext(entryInvocation.Instance, contexts, out var entryContext)
            && SymbolEqualityComparer.Default.Equals(entryContext, mutation.Entity.Context)
            && entryInvocation.Arguments.Any(argument =>
                TryResolveEntity(argument.Value, entities, out var entryEntity)
                && ReferenceEquals(entryEntity, mutation.Entity)
            )
            && PropertyInvocationMatches(
                propertyInvocation,
                mutation.Property,
                mutation.Entity.EntityType
            );
    }

    private static bool PropertyInvocationMatches(
        IInvocationOperation invocation,
        IPropertySymbol property,
        INamedTypeSymbol entityType
    )
    {
        foreach (var argument in invocation.Arguments)
        {
            if (
                argument.Value.ConstantValue is { HasValue: true, Value: string propertyName }
                    && string.Equals(propertyName, property.Name, StringComparison.Ordinal)
                || IsExactPropertyLambda(argument.Value, property, entityType)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExactPropertyLambda(
        IOperation operation,
        IPropertySymbol property,
        INamedTypeSymbol entityType
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (operation is IDelegateCreationOperation { Target: { } target })
            operation = LostUpdateOperationFacts.Unwrap(target);

        if (operation is not IAnonymousFunctionOperation anonymousFunction)
            return false;

        var parameters = anonymousFunction.Symbol.Parameters;
        var operations = anonymousFunction.Body.Operations;
        if (
            parameters.Length != 1
            || !SymbolEqualityComparer.Default.Equals(parameters[0].Type, entityType)
            || operations.Length != 1
            || operations[0] is not IReturnOperation { ReturnedValue: { } returnedValue }
            || LostUpdateOperationFacts.Unwrap(returnedValue)
                is not IPropertyReferenceOperation propertyReference
            || !SymbolEqualityComparer.Default.Equals(propertyReference.Property, property)
            || !LostUpdateOperationFacts.TryGetRootParameter(
                propertyReference.Instance,
                out var rootParameter
            )
        )
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(rootParameter, parameters[0]);
    }

    private static bool IsDefinitelyDetachedBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return false;

        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart <= mutation.Entity.MaterializationPosition
                || assignment.Syntax.SpanStart >= save.Position
            )
            {
                continue;
            }

            var isStateReset =
                TryGetEntryStateAssignment(
                    assignment,
                    mutation,
                    contexts,
                    entities,
                    out var stateName
                )
                && stateName is "Detached" or "Deleted" or "Added" or "Unchanged"
                && (stateName != "Unchanged" || assignment.Syntax.SpanStart >= mutation.Position);
            var isPropertyReset =
                assignment.Syntax.SpanStart > mutation.Position
                && TryGetIsModifiedAssignment(
                    assignment,
                    mutation,
                    contexts,
                    entities,
                    expectedValue: false
                );
            if (
                (!isStateReset && !isPropertyReset)
                || HasLaterPersistence(
                    mutation,
                    save,
                    assignment.Syntax.SpanStart,
                    assignments,
                    invocations,
                    contexts,
                    queries,
                    entities,
                    flowGraph
                )
            )
            {
                continue;
            }

            if (TransitionDominates(mutation, save, assignment, flowGraph))
                return true;
        }

        foreach (var invocation in invocations)
        {
            if (
                invocation.Syntax.SpanStart <= mutation.Entity.MaterializationPosition
                || invocation.Syntax.SpanStart >= save.Position
            )
            {
                continue;
            }

            var isRemove =
                LostUpdateOperationFacts.IsRemovalOperation(invocation.TargetMethod)
                && TryResolveInvocationContext(invocation, contexts, queries, out var removeContext)
                && SymbolEqualityComparer.Default.Equals(removeContext, mutation.Entity.Context)
                && invocation.Arguments.Any(argument =>
                    ContainsEntity(argument.Value, mutation.Entity, entities)
                );
            var isClear =
                LostUpdateOperationFacts.IsEfChangeTrackerClearMethod(invocation.TargetMethod)
                && invocation.Instance != null
                && LostUpdateOperationFacts.Unwrap(invocation.Instance)
                    is IPropertyReferenceOperation
                    {
                        Property: { } changeTrackerProperty,
                        Instance: { } contextInstance,
                    }
                && LostUpdateOperationFacts.IsEfDbContextChangeTrackerProperty(
                    changeTrackerProperty
                )
                && TryResolveContext(contextInstance, contexts, out var clearContext)
                && SymbolEqualityComparer.Default.Equals(clearContext, mutation.Entity.Context);
            var isAcceptAllChanges =
                LostUpdateOperationFacts.IsEfChangeTrackerAcceptAllChangesMethod(
                    invocation.TargetMethod
                )
                && invocation.Instance != null
                && LostUpdateOperationFacts.Unwrap(invocation.Instance)
                    is IPropertyReferenceOperation
                    {
                        Property: { } acceptTrackerProperty,
                        Instance: { } acceptContextInstance,
                    }
                && LostUpdateOperationFacts.IsEfDbContextChangeTrackerProperty(
                    acceptTrackerProperty
                )
                && TryResolveContext(acceptContextInstance, contexts, out var acceptChangesContext)
                && SymbolEqualityComparer.Default.Equals(
                    acceptChangesContext,
                    mutation.Entity.Context
                );
            var isReload =
                invocation.Syntax.SpanStart > mutation.Position
                && IsMatchingCompletedReload(invocation, mutation, contexts, entities);
            if (
                (!isRemove && !isClear && !isAcceptAllChanges && !isReload)
                || HasLaterPersistence(
                    mutation,
                    save,
                    invocation.Syntax.SpanStart,
                    assignments,
                    invocations,
                    contexts,
                    queries,
                    entities,
                    flowGraph
                )
            )
            {
                continue;
            }

            if (TransitionDominates(mutation, save, invocation, flowGraph))
                return true;
        }

        return false;
    }

    private static bool IsMatchingCompletedReload(
        IInvocationOperation invocation,
        MutationEvidence mutation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities
    )
    {
        return IsMatchingCompletedReload(invocation, mutation.Entity, contexts, entities);
    }

    private static bool IsMatchingCompletedReload(
        IInvocationOperation invocation,
        EntitySource entity,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities
    )
    {
        return LostUpdateOperationFacts.IsEfEntityEntryReloadMethod(invocation.TargetMethod)
            && IsReloadCompletionObserved(invocation)
            && invocation.Instance != null
            && LostUpdateOperationFacts.Unwrap(invocation.Instance)
                is IInvocationOperation entryInvocation
            && LostUpdateOperationFacts.IsEfDbContextEntryMethod(entryInvocation.TargetMethod)
            && TryResolveContext(entryInvocation.Instance, contexts, out var reloadContext)
            && SymbolEqualityComparer.Default.Equals(reloadContext, entity.Context)
            && entryInvocation.Arguments.Any(argument =>
                ContainsEntity(argument.Value, entity, entities)
            );
    }

    private static bool IsReloadCompletionObserved(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name == "Reload")
            return true;

        IOperation current = invocation;
        while (current.Parent != null)
        {
            switch (current.Parent)
            {
                case IConversionOperation or IParenthesizedOperation:
                    current = current.Parent;
                    continue;
                case IInvocationOperation configureAwait
                    when configureAwait.TargetMethod.Name == "ConfigureAwait":
                    current = configureAwait;
                    continue;
                case IAwaitOperation:
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryGetEntryStateAssignment(
        ISimpleAssignmentOperation assignment,
        MutationEvidence mutation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities,
        out string stateName
    )
    {
        if (
            assignment.Target
                is IPropertyReferenceOperation
                {
                    Property: { } stateProperty,
                    Instance: { } stateReceiver,
                }
            && LostUpdateOperationFacts.IsEfEntityEntryStateProperty(stateProperty)
            && LostUpdateOperationFacts.Unwrap(stateReceiver)
                is IInvocationOperation entryInvocation
            && LostUpdateOperationFacts.IsEfDbContextEntryMethod(entryInvocation.TargetMethod)
            && TryResolveContext(entryInvocation.Instance, contexts, out var entryContext)
            && SymbolEqualityComparer.Default.Equals(entryContext, mutation.Entity.Context)
            && entryInvocation.Arguments.Any(argument =>
                ContainsEntity(argument.Value, mutation.Entity, entities)
            )
            && TryGetEntityStateName(assignment.Value, out stateName)
        )
        {
            return true;
        }

        stateName = string.Empty;
        return false;
    }

    private static bool HasLaterPersistence(
        MutationEvidence mutation,
        SaveEvidence save,
        int transitionPosition,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph flowGraph
    )
    {
        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart > transitionPosition
                && assignment.Syntax.SpanStart < save.Position
                && (
                    TryGetEntryStateAssignment(
                        assignment,
                        mutation,
                        contexts,
                        entities,
                        out var stateName
                    )
                        && stateName == "Modified"
                    || TryGetIsModifiedAssignment(assignment, mutation, contexts, entities)
                )
                && TrackingSharesPath(
                    mutation,
                    save,
                    assignment,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        foreach (var invocation in invocations)
        {
            if (
                invocation.Syntax.SpanStart <= transitionPosition
                || invocation.Syntax.SpanStart >= save.Position
                || !LostUpdateOperationFacts.IsTrackingOperation(invocation.TargetMethod)
                || (
                    invocation.Syntax.SpanStart >= mutation.Position
                    && !LostUpdateOperationFacts.PersistsPriorMutation(invocation.TargetMethod)
                )
                || !TryResolveInvocationContext(
                    invocation,
                    contexts,
                    queries,
                    out var trackingContext
                )
                || !SymbolEqualityComparer.Default.Equals(trackingContext, mutation.Entity.Context)
            )
            {
                continue;
            }

            if (
                invocation.Arguments.Any(argument =>
                    ContainsEntity(argument.Value, mutation.Entity, entities)
                )
                && TrackingSharesPath(
                    mutation,
                    save,
                    invocation,
                    LostUpdateOperationFacts.PersistsPriorMutation(invocation.TargetMethod),
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveInvocationContext(
        IInvocationOperation invocation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        out ISymbol context
    )
    {
        if (TryResolveContext(invocation.Instance, contexts, out context))
            return true;

        if (
            invocation.Instance != null
            && TryResolveQuery(invocation.Instance, contexts, queries, out var query)
        )
        {
            context = query.Context;
            return true;
        }

        context = null!;
        return false;
    }

    private static bool IsProtectedByTransaction(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<TransactionEvidence> transactions,
        IEnumerable<TransactionResetEvidence> transactionResets,
        OperationCollector collector,
        LostUpdateCompilationEvidence evidence,
        SyntaxTree callerTree,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return false;

        foreach (var transaction in transactions)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(transaction.Context, mutation.Entity.Context)
            )
            {
                continue;
            }

            if (
                !OperationDominates(
                    transaction.Invocation,
                    mutation.Entity.Materialization,
                    flowGraph
                )
            )
            {
                continue;
            }

            if (ReferenceEquals(transaction.Invocation, save.Invocation))
            {
                if (
                    !save.ContainedPosition.HasValue
                    || !transaction.ProtectedSavePositions.Contains(save.ContainedPosition.Value)
                )
                {
                    continue;
                }
            }
            else if (!TransitionDominates(mutation, save, transaction.Invocation, flowGraph))
            {
                continue;
            }

            if (
                transactionResets.Any(reset =>
                    SymbolEqualityComparer.Default.Equals(reset.Context, transaction.Context)
                    && reset.Invocation.Syntax.SpanStart > transaction.Position
                    && reset.Invocation.Syntax.SpanStart < save.Position
                    && OperationCanReach(transaction.Invocation, reset.Invocation, flowGraph)
                    && OperationCanReach(reset.Invocation, save.Invocation, flowGraph)
                )
            )
            {
                continue;
            }

            if (
                !TransactionLifetimeCoversSave(
                    transaction,
                    save,
                    collector,
                    evidence,
                    callerTree,
                    flowGraph
                )
            )
                continue;

            if (TransactionConditionHoldsOnProtectedPath(transaction, save, collector, flowGraph))
                return true;
        }

        return false;
    }

    private static bool TransactionLifetimeCoversSave(
        TransactionEvidence transaction,
        SaveEvidence save,
        OperationCollector collector,
        LostUpdateCompilationEvidence evidence,
        SyntaxTree callerTree,
        ControlFlowGraph flowGraph
    )
    {
        if (ReferenceEquals(transaction.Invocation, save.Invocation))
            return true;
        var invocationSyntax = transaction.Invocation.Syntax;
        var usingStatement = invocationSyntax
            .AncestorsAndSelf()
            .OfType<UsingStatementSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Expression?.Span.Contains(invocationSyntax.Span) == true
                || candidate.Declaration?.Span.Contains(invocationSyntax.Span) == true
            );
        if (
            usingStatement != null
            && !usingStatement.Statement.Span.Contains(save.Invocation.Syntax.Span)
        )
        {
            return false;
        }

        var usingDeclaration = invocationSyntax
            .AncestorsAndSelf()
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault(candidate =>
                !candidate.UsingKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
                && candidate.Declaration.Span.Contains(invocationSyntax.Span)
            );
        if (
            usingDeclaration != null
            && (
                usingDeclaration.Parent is not BlockSyntax usingScope
                || !usingScope.Span.Contains(save.Invocation.Syntax.Span)
            )
        )
        {
            return false;
        }

        if (
            !TryGetTransactionLocal(
                transaction.Invocation,
                out var transactionLocal,
                out var transactionOrigin
            )
        )
            return usingStatement != null || usingDeclaration != null;

        if (
            !TryGetStableTransactionAliases(
                transaction,
                save,
                transactionLocal,
                transactionOrigin,
                collector,
                evidence,
                callerTree,
                out var aliases
            )
        )
        {
            return false;
        }

        return !collector.Invocations.Any(termination =>
            termination.Syntax.SpanStart > transaction.Position
            && termination.Syntax.SpanStart < save.Position
            && IsTransactionTermination(termination, aliases, flowGraph)
            && OperationCanReach(transaction.Invocation, termination, flowGraph)
            && OperationCanReach(termination, save.Invocation, flowGraph)
        );
    }

    private static bool TryGetTransactionLocal(
        IInvocationOperation invocation,
        out ILocalSymbol local,
        out IOperation origin
    )
    {
        IOperation current = TryGetSynchronousCompletionOperation(invocation, out var completion)
            ? completion
            : invocation;
        while (current.Parent != null)
        {
            switch (current.Parent)
            {
                case IConversionOperation conversion
                    when ReferenceEquals(conversion.Operand, current):
                case IParenthesizedOperation parenthesized
                    when ReferenceEquals(parenthesized.Operand, current):
                case IAwaitOperation awaitOperation
                    when ContainsOperation(awaitOperation.Operation, current):
                    current = current.Parent;
                    continue;
                case IInvocationOperation configuredAwait
                    when LostUpdateOperationFacts.IsFrameworkConfigureAwait(
                        configuredAwait.TargetMethod
                    )
                        && configuredAwait.Instance != null
                        && ContainsOperation(configuredAwait.Instance, current):
                    current = configuredAwait;
                    continue;
                case IVariableInitializerOperation initializer
                    when initializer.Parent is IVariableDeclaratorOperation declarator:
                    local = declarator.Symbol;
                    origin = initializer.Value;
                    return true;
                case ISimpleAssignmentOperation
                {
                    Target: ILocalReferenceOperation localReference,
                    Value: { } value,
                } assignment when ContainsOperation(value, current):
                    local = localReference.Local;
                    origin = assignment;
                    return true;
                default:
                    local = null!;
                    origin = null!;
                    return false;
            }
        }

        local = null!;
        origin = null!;
        return false;
    }

    private static bool TryGetStableTransactionAliases(
        TransactionEvidence transaction,
        SaveEvidence save,
        ILocalSymbol transactionLocal,
        IOperation transactionOrigin,
        OperationCollector collector,
        LostUpdateCompilationEvidence evidence,
        SyntaxTree callerTree,
        out Dictionary<ILocalSymbol, IOperation> aliases
    )
    {
        aliases = new Dictionary<ILocalSymbol, IOperation>(SymbolEqualityComparer.Default)
        {
            [transactionLocal] = transactionOrigin,
        };

        var changed = true;
        while (changed)
        {
            changed = false;

            foreach (var declarator in collector.Declarators)
            {
                var value = declarator.Initializer?.Value;
                if (
                    value == null
                    || !IsBetween(value, transaction.Position, save.Position)
                    || aliases.ContainsKey(declarator.Symbol)
                )
                {
                    continue;
                }

                if (TryGetReferencedAlias(value, aliases, out _))
                {
                    if (
                        !IsStableTransactionAlias(
                            declarator.Symbol,
                            value,
                            transaction,
                            save,
                            collector,
                            evidence,
                            callerTree
                        )
                    )
                    {
                        return false;
                    }

                    aliases.Add(declarator.Symbol, value);
                    changed = true;
                }
                else if (ContainsTransactionAliasReference(value, aliases))
                {
                    return false;
                }
            }

            foreach (var assignment in collector.SimpleAssignments)
            {
                if (
                    assignment.Target is not ILocalReferenceOperation target
                    || !IsBetween(assignment, transaction.Position, save.Position)
                    || aliases.ContainsKey(target.Local)
                )
                {
                    continue;
                }

                if (TryGetReferencedAlias(assignment.Value, aliases, out _))
                {
                    if (
                        !IsStableTransactionAlias(
                            target.Local,
                            assignment,
                            transaction,
                            save,
                            collector,
                            evidence,
                            callerTree
                        )
                    )
                    {
                        return false;
                    }

                    aliases.Add(target.Local, assignment);
                    changed = true;
                }
                else if (ContainsTransactionAliasReference(assignment.Value, aliases))
                {
                    return false;
                }
            }
        }

        foreach (var alias in aliases)
        {
            if (
                !IsStableTransactionAlias(
                    alias.Key,
                    alias.Value,
                    transaction,
                    save,
                    collector,
                    evidence,
                    callerTree
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStableTransactionAlias(
        ILocalSymbol local,
        IOperation origin,
        TransactionEvidence transaction,
        SaveEvidence save,
        OperationCollector collector,
        LostUpdateCompilationEvidence evidence,
        SyntaxTree callerTree
    )
    {
        return !collector.Declarators.Any(declarator =>
                SymbolEqualityComparer.Default.Equals(declarator.Symbol, local)
                && declarator.Initializer?.Value is { } value
                && IsBetween(value, transaction.Position, save.Position)
                && !ReferenceEquals(value, origin)
            )
            && !collector.SimpleAssignments.Any(assignment =>
                IsBetween(assignment, transaction.Position, save.Position)
                && IsSymbolReference(assignment.Target, local)
                && !ReferenceEquals(assignment, origin)
            )
            && !collector.CompoundAssignments.Any(assignment =>
                IsBetween(assignment, transaction.Position, save.Position)
                && IsSymbolReference(assignment.Target, local)
            )
            && !collector.CoalesceAssignments.Any(assignment =>
                IsBetween(assignment, transaction.Position, save.Position)
                && IsSymbolReference(assignment.Target, local)
            )
            && !collector.Increments.Any(increment =>
                IsBetween(increment, transaction.Position, save.Position)
                && IsSymbolReference(increment.Target, local)
            )
            && !collector.Invocations.Any(invocation =>
                IsBetween(invocation, transaction.Position, save.Position)
                && invocation.Arguments.Any(argument =>
                    IsSymbolReference(argument.Value, local)
                    && (
                        argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                        || argument.Parameter == null
                        || !evidence.PrivateHelperPreservesTransaction(
                            invocation,
                            argument.Parameter.Ordinal,
                            callerTree
                        )
                    )
                )
            );
    }

    private static bool TryGetReferencedAlias(
        IOperation operation,
        Dictionary<ILocalSymbol, IOperation> aliases,
        out IOperation origin
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation is ILocalReferenceOperation local
            && aliases.TryGetValue(local.Local, out origin)
        )
        {
            return true;
        }

        origin = null!;
        return false;
    }

    private static bool ContainsTransactionAliasReference(
        IOperation operation,
        Dictionary<ILocalSymbol, IOperation> aliases
    )
    {
        if (operation is ILocalReferenceOperation local && aliases.ContainsKey(local.Local))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsTransactionAliasReference(child, aliases))
                return true;
        }

        return false;
    }

    private static bool IsTransactionTermination(
        IInvocationOperation invocation,
        Dictionary<ILocalSymbol, IOperation> aliases,
        ControlFlowGraph flowGraph
    )
    {
        if (
            !LostUpdateOperationFacts.IsTransactionTerminationMethod(invocation.TargetMethod)
            || invocation.Instance == null
            || LostUpdateOperationFacts.Unwrap(invocation.Instance)
                is not ILocalReferenceOperation localReference
            || !aliases.TryGetValue(localReference.Local, out var origin)
        )
        {
            return false;
        }

        return ReferenceEquals(origin, invocation)
            || OperationCanReach(origin, invocation, flowGraph);
    }

    private static bool TransactionConditionHoldsOnProtectedPath(
        TransactionEvidence transaction,
        SaveEvidence save,
        OperationCollector collector,
        ControlFlowGraph flowGraph
    )
    {
        if (transaction.Condition == null)
            return true;
        if (ReferenceEquals(transaction.Invocation, save.Invocation))
            return false;
        if (
            !TryGetBooleanSymbolPredicate(
                transaction.Condition,
                transaction.ConditionValue,
                out var conditionSymbol,
                out var requiredValue
            )
            || !IsBooleanSymbolStableBetween(
                conditionSymbol,
                transaction.Position,
                save.Position,
                collector
            )
        )
        {
            return false;
        }

        var transactionBlock = FindContainingBlock(flowGraph, transaction.Invocation);
        var saveBlock = FindContainingBlock(flowGraph, save.Invocation);
        if (transactionBlock == null || saveBlock == null)
            return false;

        var oppositeValue = !requiredValue;
        if (!CanReach(flowGraph.Blocks[0], transactionBlock, conditionSymbol, oppositeValue))
        {
            return true;
        }

        return !ReferenceEquals(transactionBlock, saveBlock)
            && !CanReachAfterLeaving(transactionBlock, saveBlock, conditionSymbol, oppositeValue);
    }

    private static bool TryGetBooleanSymbolPredicate(
        IOperation operation,
        bool requiredExpressionValue,
        out ISymbol symbol,
        out bool requiredSymbolValue
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        switch (operation)
        {
            case IParameterReferenceOperation parameter
                when parameter.Parameter.Type.SpecialType == SpecialType.System_Boolean:
                symbol = parameter.Parameter;
                requiredSymbolValue = requiredExpressionValue;
                return true;
            case ILocalReferenceOperation local
                when local.Local.Type.SpecialType == SpecialType.System_Boolean:
                symbol = local.Local;
                requiredSymbolValue = requiredExpressionValue;
                return true;
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: var operand }:
                return TryGetBooleanSymbolPredicate(
                    operand,
                    !requiredExpressionValue,
                    out symbol,
                    out requiredSymbolValue
                );
            case IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals,
                LeftOperand: var left,
                RightOperand: { ConstantValue: { HasValue: true, Value: bool rightValue } },
            } binary:
                var leftValue =
                    binary.OperatorKind == BinaryOperatorKind.Equals
                        ? requiredExpressionValue == rightValue
                        : requiredExpressionValue != rightValue;
                return TryGetBooleanSymbolPredicate(
                    left,
                    leftValue,
                    out symbol,
                    out requiredSymbolValue
                );
            default:
                symbol = null!;
                requiredSymbolValue = false;
                return false;
        }
    }

    private static ImmutableArray<BooleanPredicate> GetRequiredBooleanPredicates(
        IOperation operation
    )
    {
        var semanticModel = operation.SemanticModel;
        if (semanticModel == null)
            return ImmutableArray<BooleanPredicate>.Empty;

        var builder = ImmutableArray.CreateBuilder<BooleanPredicate>();
        foreach (var ifStatement in operation.Syntax.Ancestors().OfType<IfStatementSyntax>())
        {
            bool requiredExpressionValue;
            if (ifStatement.Statement.Span.Contains(operation.Syntax.Span))
            {
                requiredExpressionValue = true;
            }
            else if (ifStatement.Else?.Statement.Span.Contains(operation.Syntax.Span) == true)
            {
                requiredExpressionValue = false;
            }
            else
            {
                continue;
            }

            var condition = semanticModel.GetOperation(ifStatement.Condition);
            if (
                condition != null
                && TryGetBooleanSymbolPredicate(
                    condition,
                    requiredExpressionValue,
                    out var symbol,
                    out var value
                )
                && !builder.Any(predicate =>
                    SymbolEqualityComparer.Default.Equals(predicate.Symbol, symbol)
                    && predicate.Value == value
                )
            )
            {
                builder.Add(new BooleanPredicate(symbol, value));
            }
        }

        return builder.ToImmutable();
    }

    private static bool OperationRequiresBooleanPredicate(
        IOperation operation,
        BooleanPredicate expected
    )
    {
        return GetRequiredBooleanPredicates(operation)
            .Any(predicate =>
                SymbolEqualityComparer.Default.Equals(predicate.Symbol, expected.Symbol)
                && predicate.Value == expected.Value
            );
    }

    private static bool IsBooleanSymbolStableBetween(
        ISymbol symbol,
        int startPosition,
        int endPosition,
        OperationCollector collector
    )
    {
        return !collector.SimpleAssignments.Any(assignment =>
                IsBetween(assignment, startPosition, endPosition)
                && IsSymbolReference(assignment.Target, symbol)
            )
            && !collector.CompoundAssignments.Any(assignment =>
                IsBetween(assignment, startPosition, endPosition)
                && IsSymbolReference(assignment.Target, symbol)
            )
            && !collector.CoalesceAssignments.Any(assignment =>
                IsBetween(assignment, startPosition, endPosition)
                && IsSymbolReference(assignment.Target, symbol)
            )
            && !collector.Increments.Any(increment =>
                IsBetween(increment, startPosition, endPosition)
                && IsSymbolReference(increment.Target, symbol)
            )
            && !collector.Invocations.Any(invocation =>
                IsBetween(invocation, startPosition, endPosition)
                && invocation.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && IsSymbolReference(argument.Value, symbol)
                )
            );
    }

    private static bool IsBetween(IOperation operation, int startPosition, int endPosition)
    {
        return operation.Syntax.SpanStart > startPosition
            && operation.Syntax.SpanStart < endPosition;
    }

    private static bool IsContextStableForSave(
        EntitySource entity,
        SaveEvidence save,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        if (
            !TryGetContextRootCapture(
                entity.ContextAccess,
                entity.Context,
                entity.Materialization,
                collector,
                flowGraph,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out var entityCapture,
                out var entityCaptureIsOpaque
            )
            || !TryGetContextRootCapture(
                save.ContextAccess,
                save.Context,
                save.Invocation,
                collector,
                flowGraph,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out var saveCapture,
                out var saveCaptureIsOpaque
            )
        )
        {
            return false;
        }

        if (ReferenceEquals(entityCapture, saveCapture))
            return true;
        if (entityCaptureIsOpaque || saveCaptureIsOpaque)
            return false;

        var first =
            entityCapture.Syntax.SpanStart <= saveCapture.Syntax.SpanStart
                ? entityCapture
                : saveCapture;
        var second = ReferenceEquals(first, entityCapture) ? saveCapture : entityCapture;
        return !HasContextRootWriteBetween(entity.Context, first, second, collector, flowGraph);
    }

    private static bool TryGetContextRootCapture(
        ISymbol contextAccess,
        ISymbol contextRoot,
        IOperation destination,
        OperationCollector collector,
        ControlFlowGraph? flowGraph,
        HashSet<ISymbol> visiting,
        out IOperation capture,
        out bool captureIsOpaque
    )
    {
        if (SymbolEqualityComparer.Default.Equals(contextAccess, contextRoot))
        {
            capture = destination;
            captureIsOpaque = false;
            return true;
        }

        if (contextAccess is not ILocalSymbol local || !visiting.Add(local))
        {
            capture = null!;
            captureIsOpaque = false;
            return false;
        }

        var declaration = collector.Declarators.FirstOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.Symbol, local)
        );
        if (declaration?.Initializer?.Value is not { } value)
        {
            capture = null!;
            captureIsOpaque = false;
            return false;
        }

        var writes = new List<IOperation>();
        writes.AddRange(
            collector.SimpleAssignments.Where(assignment =>
                assignment.Syntax.SpanStart > declaration.Syntax.SpanStart
                && assignment.Syntax.SpanStart < destination.Syntax.SpanStart
                && IsSymbolReference(assignment.Target, local)
                && CanContextWriteAffect(assignment, destination, collector, flowGraph)
            )
        );
        writes.AddRange(
            collector.Invocations.Where(invocation =>
                invocation.Syntax.SpanStart > declaration.Syntax.SpanStart
                && invocation.Syntax.SpanStart < destination.Syntax.SpanStart
                && invocation.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && IsSymbolReference(argument.Value, local)
                )
                && CanContextWriteAffect(invocation, destination, collector, flowGraph)
            )
        );
        if (writes.Count != 0)
        {
            var latest = writes.OrderByDescending(write => write.Syntax.SpanStart).First();
            if (flowGraph != null && !OperationDominates(latest, destination, flowGraph))
            {
                capture = null!;
                captureIsOpaque = false;
                return false;
            }

            capture = latest;
            captureIsOpaque = true;
            return true;
        }

        if (!TryGetContextAccessSymbol(value, out var sourceAccess))
        {
            capture = null!;
            captureIsOpaque = false;
            return false;
        }

        return TryGetContextRootCapture(
            sourceAccess,
            contextRoot,
            value,
            collector,
            flowGraph,
            visiting,
            out capture,
            out captureIsOpaque
        );
    }

    private static bool HasContextRootWriteBetween(
        ISymbol contextRoot,
        IOperation first,
        IOperation second,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        bool IsRelevantWrite(IOperation write)
        {
            return IsBetween(write, first.Syntax.SpanStart, second.Syntax.SpanStart)
                && CanContextWriteAffect(first, write, collector, flowGraph)
                && CanContextWriteAffect(write, second, collector, flowGraph);
        }

        return collector.SimpleAssignments.Any(assignment =>
                IsSymbolReference(assignment.Target, contextRoot) && IsRelevantWrite(assignment)
            )
            || collector.CompoundAssignments.Any(assignment =>
                IsSymbolReference(assignment.Target, contextRoot) && IsRelevantWrite(assignment)
            )
            || collector.CoalesceAssignments.Any(assignment =>
                IsSymbolReference(assignment.Target, contextRoot) && IsRelevantWrite(assignment)
            )
            || collector.Increments.Any(increment =>
                IsSymbolReference(increment.Target, contextRoot) && IsRelevantWrite(increment)
            )
            || collector.Invocations.Any(invocation =>
                IsRelevantWrite(invocation)
                && (
                    invocation.Arguments.Any(argument =>
                        argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                        && IsSymbolReference(argument.Value, contextRoot)
                    )
                    || contextRoot is IFieldSymbol field
                        && InvocationMayRebindContextField(invocation, field)
                )
            );
    }

    private static bool InvocationMayRebindContextField(
        IInvocationOperation invocation,
        IFieldSymbol field
    )
    {
        var compilation = invocation.SemanticModel?.Compilation;
        return InvocationMayRebindContextField(
            invocation,
            field,
            compilation,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
        );
    }

    private static bool InvocationMayRebindContextField(
        IInvocationOperation invocation,
        IFieldSymbol field,
        Compilation? compilation,
        HashSet<IMethodSymbol> visiting
    )
    {
        if (
            invocation.Arguments.Any(argument =>
                argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                && IsSymbolReference(argument.Value, field)
            )
        )
        {
            return true;
        }

        var method = invocation.TargetMethod.OriginalDefinition;
        var canAccessContainingInstance =
            method.MethodKind == MethodKind.DelegateInvoke
            || ContainsCurrentInstance(invocation.Instance, field.ContainingType)
            || invocation.Arguments.Any(argument =>
                ContainsCurrentInstance(argument.Value, field.ContainingType)
            );
        if (
            canAccessContainingInstance
            && (
                method.IsAbstract
                || method.IsVirtual && !method.IsSealed && !method.ContainingType.IsSealed
            )
        )
        {
            return true;
        }

        if (
            compilation == null
            || !visiting.Add(method)
            || !TryGetSourceMethodBody(method, compilation, out var body)
        )
        {
            return canAccessContainingInstance;
        }

        return OperationMayRebindContextField(body, field, compilation, visiting);
    }

    private static bool OperationMayRebindContextField(
        IOperation operation,
        IFieldSymbol field,
        Compilation compilation,
        HashSet<IMethodSymbol> visiting
    )
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            return false;

        if (
            operation is ISimpleAssignmentOperation simple
                && IsSymbolReference(simple.Target, field)
            || operation is ICompoundAssignmentOperation compound
                && IsSymbolReference(compound.Target, field)
            || operation is ICoalesceAssignmentOperation coalesce
                && IsSymbolReference(coalesce.Target, field)
            || operation is IIncrementOrDecrementOperation increment
                && IsSymbolReference(increment.Target, field)
        )
        {
            return true;
        }

        if (
            operation is IInvocationOperation invocation
            && InvocationMayRebindContextField(invocation, field, compilation, visiting)
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (OperationMayRebindContextField(child, field, compilation, visiting))
                return true;
        }

        return false;
    }

    private static bool TryGetSourceMethodBody(
        IMethodSymbol method,
        Compilation compilation,
        out IOperation body
    )
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var candidate = syntax switch
            {
                MethodDeclarationSyntax methodSyntax => model.GetOperation(methodSyntax)
                    ?? (
                        methodSyntax.Body != null ? model.GetOperation(methodSyntax.Body)
                        : methodSyntax.ExpressionBody != null
                            ? model.GetOperation(methodSyntax.ExpressionBody.Expression)
                        : null
                    ),
                LocalFunctionStatementSyntax localFunction => model.GetOperation(localFunction)
                    ?? (
                        localFunction.Body != null ? model.GetOperation(localFunction.Body)
                        : localFunction.ExpressionBody != null
                            ? model.GetOperation(localFunction.ExpressionBody.Expression)
                        : null
                    ),
                _ => null,
            };
            if (candidate != null)
            {
                body = candidate;
                return true;
            }
        }

        body = null!;
        return false;
    }

    private static bool ContainsCurrentInstance(IOperation? operation, INamedTypeSymbol type)
    {
        if (operation == null)
            return false;
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation is IInstanceReferenceOperation instance
            && SymbolEqualityComparer.Default.Equals(instance.Type, type);
    }

    private static bool IsContextWriteInWindow(
        IOperation write,
        IOperation target,
        ISymbol contextAccess,
        int originPosition,
        IOperation destination,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        return write.Syntax.SpanStart > originPosition
            && write.Syntax.SpanStart < destination.Syntax.SpanStart
            && IsSymbolReference(target, contextAccess)
            && CanContextWriteAffect(write, destination, collector, flowGraph);
    }

    private static bool CanContextWriteAffect(
        IOperation write,
        IOperation destination,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return true;
        if (!OperationCanReach(write, destination, flowGraph))
            return false;

        var writePredicates = GetRequiredBooleanPredicates(write);
        var destinationPredicates = GetRequiredBooleanPredicates(destination);
        foreach (var writePredicate in writePredicates)
        {
            foreach (var destinationPredicate in destinationPredicates)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        writePredicate.Symbol,
                        destinationPredicate.Symbol
                    )
                    && writePredicate.Value != destinationPredicate.Value
                    && IsBooleanSymbolStableBetween(
                        writePredicate.Symbol,
                        write.Syntax.SpanStart,
                        destination.Syntax.SpanStart,
                        collector
                    )
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsSymbolReference(IOperation operation, ISymbol symbol)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation is IParameterReferenceOperation parameter
                && SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol)
            || operation is ILocalReferenceOperation local
                && SymbolEqualityComparer.Default.Equals(local.Local, symbol)
            || operation is IFieldReferenceOperation field
                && SymbolEqualityComparer.Default.Equals(field.Field, symbol);
    }

    private static bool TransitionDominates(
        MutationEvidence mutation,
        SaveEvidence save,
        IOperation transition,
        ControlFlowGraph flowGraph
    )
    {
        var mutationBlock = FindContainingBlock(flowGraph, mutation.Operation);
        var transitionBlock = FindContainingBlock(flowGraph, transition);
        var saveBlock = FindContainingBlock(flowGraph, save.Invocation);
        if (
            mutationBlock == null
            || transitionBlock == null
            || saveBlock == null
            || ReferenceEquals(transitionBlock, saveBlock)
                && transition.Syntax.SpanStart >= save.Invocation.Syntax.SpanStart
        )
            return false;

        if (transition.Syntax.SpanStart < mutation.Position)
        {
            return CanReach(flowGraph.Blocks[0], transitionBlock)
                && CanReach(transitionBlock, mutationBlock)
                && !CanReachAvoiding(flowGraph.Blocks[0], mutationBlock, transitionBlock)
                && CanReach(mutationBlock, saveBlock);
        }

        return CanReach(mutationBlock, transitionBlock)
            && CanReach(transitionBlock, saveBlock)
            && !CanReachAvoiding(mutationBlock, saveBlock, transitionBlock);
    }

    private static bool IsPersistingEntityState(IOperation operation)
    {
        return IsEntityState(operation, "Modified");
    }

    private static bool IsEntityState(IOperation operation, params string[] stateNames)
    {
        return TryGetEntityStateName(operation, out var stateName)
            && stateNames.Contains(stateName, StringComparer.Ordinal);
    }

    private static bool TryGetEntityStateName(IOperation operation, out string stateName)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation
                is IFieldReferenceOperation
                {
                    Field:
                    {
                        Name: var name,
                        ContainingType:
                        { Name: "EntityState", ContainingNamespace: { } entityStateNamespace },
                    },
                }
            && entityStateNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore"
        )
        {
            stateName = name;
            return true;
        }

        stateName = string.Empty;
        return false;
    }

    private static bool TrackingSharesPath(
        MutationEvidence mutation,
        SaveEvidence save,
        IOperation trackingOperation,
        bool permitsAfterMutation,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return false;

        var trackingBlock = FindContainingBlock(flowGraph, trackingOperation);
        var mutationBlock = FindContainingBlock(flowGraph, mutation.Operation);
        var saveBlock = FindContainingBlock(flowGraph, save.Invocation);
        if (trackingBlock == null || mutationBlock == null || saveBlock == null)
            return false;

        if (trackingOperation.Syntax.SpanStart < mutation.Position)
        {
            return CanReach(flowGraph.Blocks[0], trackingBlock)
                && CanReach(trackingBlock, mutationBlock)
                && CanReach(mutationBlock, saveBlock);
        }

        return permitsAfterMutation
            && CanReach(flowGraph.Blocks[0], mutationBlock)
            && CanReach(mutationBlock, trackingBlock)
            && CanReach(trackingBlock, saveBlock);
    }

    private static bool IsAutoDetectionDisabledBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return false;

        var effectiveAssignment = assignments
            .Where(assignment =>
                assignment.Syntax.SpanStart < save.Position
                && TryGetAutoDetectChangesAssignment(
                    assignment,
                    mutation.Entity.Context,
                    contexts,
                    out _
                )
                && OperationDominates(assignment, save.Invocation, flowGraph)
            )
            .OrderByDescending(assignment => assignment.Syntax.SpanStart)
            .FirstOrDefault();
        if (
            effectiveAssignment == null
            || !TryGetAutoDetectChangesAssignment(
                effectiveAssignment,
                mutation.Entity.Context,
                contexts,
                out var enabled
            )
            || enabled
        )
        {
            return false;
        }

        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart > effectiveAssignment.Syntax.SpanStart
                && IsAutoDetectChangesAssignment(assignment, mutation.Entity.Context, contexts)
                && (
                    !TryGetAutoDetectChangesAssignment(
                        assignment,
                        mutation.Entity.Context,
                        contexts,
                        out var laterEnabled
                    ) || laterEnabled
                )
                && (
                    assignment.Syntax.SpanStart < save.Position
                        && TrackingSharesPath(
                            mutation,
                            save,
                            assignment,
                            permitsAfterMutation: true,
                            flowGraph
                        )
                    || assignment.Syntax.SpanStart > save.Position
                        && OperationCanReach(mutation.Operation, assignment, flowGraph)
                        && OperationCanReachWithoutPassingThrough(
                            assignment,
                            save.Invocation,
                            effectiveAssignment,
                            flowGraph
                        )
                )
            )
            {
                return false;
            }
        }

        foreach (var invocation in invocations)
        {
            if (
                invocation.Syntax.SpanStart < save.Position
                && (
                    invocation.Syntax.SpanStart > mutation.Position
                        && IsMatchingDetectChanges(invocation, mutation.Entity.Context, contexts)
                    || invocation.Syntax.SpanStart > mutation.Entity.MaterializationPosition
                        && IsMatchingUpdate(invocation, mutation, contexts, queries, entities)
                )
                && TrackingSharesPath(
                    mutation,
                    save,
                    invocation,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                return false;
            }
        }

        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart > mutation.Entity.MaterializationPosition
                && assignment.Syntax.SpanStart < save.Position
                && (
                    TryGetIsModifiedAssignment(assignment, mutation, contexts, entities)
                    || TryGetEntryStateAssignment(
                        assignment,
                        mutation,
                        contexts,
                        entities,
                        out var stateName
                    )
                        && stateName == "Modified"
                )
                && TrackingSharesPath(
                    mutation,
                    save,
                    assignment,
                    permitsAfterMutation: true,
                    flowGraph
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAutoDetectChangesAssignment(
        ISimpleAssignmentOperation assignment,
        ISymbol expectedContext,
        Dictionary<ILocalSymbol, ISymbol> contexts
    )
    {
        return assignment.Target
                is IPropertyReferenceOperation
                {
                    Property: { } autoDetectProperty,
                    Instance: { } receiver,
                }
            && LostUpdateOperationFacts.IsEfChangeTrackerProperty(
                autoDetectProperty,
                "AutoDetectChangesEnabled"
            )
            && LostUpdateOperationFacts.Unwrap(receiver)
                is IPropertyReferenceOperation
                {
                    Property: { } changeTrackerProperty,
                    Instance: { } contextInstance,
                }
            && LostUpdateOperationFacts.IsEfDbContextChangeTrackerProperty(changeTrackerProperty)
            && TryResolveContext(contextInstance, contexts, out var context)
            && SymbolEqualityComparer.Default.Equals(context, expectedContext);
    }

    private static bool TryGetAutoDetectChangesAssignment(
        ISimpleAssignmentOperation assignment,
        ISymbol expectedContext,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        out bool enabled
    )
    {
        if (
            IsAutoDetectChangesAssignment(assignment, expectedContext, contexts)
            && assignment.Value.ConstantValue is { HasValue: true, Value: bool value }
        )
        {
            enabled = value;
            return true;
        }

        enabled = false;
        return false;
    }

    private static bool IsMatchingDetectChanges(
        IInvocationOperation invocation,
        ISymbol expectedContext,
        Dictionary<ILocalSymbol, ISymbol> contexts
    )
    {
        return LostUpdateOperationFacts.IsEfChangeTrackerDetectChangesMethod(
                invocation.TargetMethod
            )
            && invocation.Instance != null
            && LostUpdateOperationFacts.Unwrap(invocation.Instance)
                is IPropertyReferenceOperation
                {
                    Property: { } changeTrackerProperty,
                    Instance: { } contextInstance,
                }
            && LostUpdateOperationFacts.IsEfDbContextChangeTrackerProperty(changeTrackerProperty)
            && TryResolveContext(contextInstance, contexts, out var context)
            && SymbolEqualityComparer.Default.Equals(context, expectedContext);
    }

    private static bool IsMatchingUpdate(
        IInvocationOperation invocation,
        MutationEvidence mutation,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, QuerySource> queries,
        Dictionary<ILocalSymbol, EntitySource> entities
    )
    {
        return LostUpdateOperationFacts.PersistsPriorMutation(invocation.TargetMethod)
            && TryResolveInvocationContext(invocation, contexts, queries, out var updateContext)
            && SymbolEqualityComparer.Default.Equals(updateContext, mutation.Entity.Context)
            && invocation.Arguments.Any(argument =>
                ContainsEntity(argument.Value, mutation.Entity, entities)
            );
    }

    private static bool IsDefinitelyInitializedBeforeMutation(
        MutationEvidence mutation,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph,
        LoadedValueAnalysis loadedValues
    )
    {
        if (flowGraph == null)
            return false;

        if (
            mutation.Operation is ISimpleAssignmentOperation mutationAssignment
            && loadedValues.ContainsCaptured(
                mutationAssignment.Value,
                mutation.Property,
                mutation.Entity
            )
        )
        {
            return false;
        }

        foreach (var assignment in assignments)
        {
            if (
                assignment.Syntax.SpanStart >= mutation.Position
                || assignment.Target is not IPropertyReferenceOperation target
                || !SymbolEqualityComparer.Default.Equals(target.Property, mutation.Property)
                || !TryResolveEntity(target.Instance, entities, out var assignedEntity)
                || !ReferenceEquals(assignedEntity, mutation.Entity)
                || !AssignmentCompletesOnEveryPathToDestination(
                    assignment,
                    target.Property,
                    mutation.Operation
                )
                || loadedValues.Contains(assignment.Value, target.Property, assignedEntity)
                || IsGuardedByEntityPropertyRead(
                    assignment,
                    target.Property,
                    assignedEntity,
                    entities,
                    flowGraph,
                    loadedValues
                )
            )
            {
                continue;
            }

            if (
                OperationDominates(assignment, mutation.Operation, flowGraph)
                && !HasCompletedMatchingReloadBetween(
                    assignment,
                    mutation,
                    invocations,
                    contexts,
                    entities,
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCompletedMatchingReloadBetween(
        ISimpleAssignmentOperation assignment,
        MutationEvidence mutation,
        IEnumerable<IInvocationOperation> invocations,
        Dictionary<ILocalSymbol, ISymbol> contexts,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph flowGraph
    )
    {
        return invocations.Any(invocation =>
            IsMatchingCompletedReload(invocation, mutation, contexts, entities)
            && OperationCanReach(assignment, invocation, flowGraph)
            && OperationCanReach(invocation, mutation.Operation, flowGraph)
        );
    }

    private static bool IsDefinitelyOverwrittenBeforeSave(
        MutationEvidence mutation,
        SaveEvidence save,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        Dictionary<ILocalSymbol, EntitySource> entities,
        ControlFlowGraph? flowGraph,
        LoadedValueAnalysis loadedValues
    )
    {
        if (flowGraph == null)
            return false;

        var mutationBlock = FindContainingBlock(flowGraph, mutation.Operation);
        var saveBlock = FindContainingBlock(flowGraph, save.Invocation);
        if (mutationBlock == null || saveBlock == null)
            return false;

        foreach (var assignment in assignments)
        {
            if (
                assignment.Target is not IPropertyReferenceOperation target
                || !SymbolEqualityComparer.Default.Equals(target.Property, mutation.Property)
                || !TryResolveEntity(target.Instance, entities, out var assignedEntity)
                || !ReferenceEquals(assignedEntity, mutation.Entity)
                || !AssignmentCompletesOnEveryPathToDestination(
                    assignment,
                    target.Property,
                    save.Invocation
                )
                || loadedValues.Contains(assignment.Value, target.Property, assignedEntity)
                || IsGuardedByEntityPropertyRead(
                    assignment,
                    target.Property,
                    assignedEntity,
                    entities,
                    flowGraph,
                    loadedValues
                )
            )
            {
                continue;
            }

            var overwriteBlock = FindContainingBlock(flowGraph, assignment);
            if (overwriteBlock == null)
                continue;

            if (ReferenceEquals(mutationBlock, saveBlock))
            {
                if (
                    ReferenceEquals(overwriteBlock, mutationBlock)
                    && (
                        save.Position > mutation.Position
                            && assignment.Syntax.SpanStart > mutation.Position
                            && assignment.Syntax.SpanStart < save.Position
                        || save.Position < mutation.Position
                            && assignment.Syntax.SpanStart < save.Position
                            && CanReachAfterLeaving(mutationBlock, saveBlock)
                    )
                )
                {
                    return true;
                }

                if (
                    !ReferenceEquals(overwriteBlock, mutationBlock)
                    && CanReachAfterLeaving(mutationBlock, overwriteBlock)
                    && CanReach(overwriteBlock, saveBlock)
                    && !CanReachAfterLeavingAvoiding(mutationBlock, saveBlock, overwriteBlock)
                )
                {
                    return true;
                }

                continue;
            }

            if (
                ReferenceEquals(overwriteBlock, mutationBlock)
                    && assignment.Syntax.SpanStart > mutation.Position
                || ReferenceEquals(overwriteBlock, saveBlock)
                    && assignment.Syntax.SpanStart < save.Position
                || !ReferenceEquals(overwriteBlock, mutationBlock)
                    && !ReferenceEquals(overwriteBlock, saveBlock)
                    && CanReach(mutationBlock, overwriteBlock)
                    && CanReach(overwriteBlock, saveBlock)
                    && !CanReachAvoiding(mutationBlock, saveBlock, overwriteBlock)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool AssignmentCompletesOnEveryPathToDestination(
        ISimpleAssignmentOperation assignment,
        IPropertySymbol property,
        IOperation destination
    )
    {
        if (
            IsDefinitelyNonThrowing(assignment.Value)
            && IsDefinitelyNonThrowingSetter(property.SetMethod, assignment.SemanticModel)
        )
        {
            return true;
        }

        return !CanExceptionBypassCompletedAssignment(assignment, destination);
    }

    private static bool IsDefinitelyNonThrowingSetter(
        IMethodSymbol? setter,
        SemanticModel? callerModel
    )
    {
        if (setter?.DeclaringSyntaxReferences.Length != 1 || callerModel == null)
            return false;

        if (
            setter.DeclaringSyntaxReferences[0].GetSyntax()
            is not AccessorDeclarationSyntax accessor
        )
        {
            return false;
        }

        if (accessor.SemicolonToken.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken))
            return true;

        SyntaxNode? statement = accessor.ExpressionBody?.Expression;
        if (accessor.Body != null)
        {
            if (accessor.Body.Statements.Count == 0)
                return true;
            if (accessor.Body.Statements.Count != 1)
                return false;
            statement = accessor.Body.Statements[0];
        }

        if (statement == null)
            return false;

        var semanticModel = callerModel.Compilation.GetSemanticModel(accessor.SyntaxTree);
        var operation = semanticModel.GetOperation(statement);
        if (operation is IExpressionStatementOperation expressionStatement)
            operation = expressionStatement.Operation;

        return operation
                is ISimpleAssignmentOperation
                {
                    Target: IFieldReferenceOperation field,
                    Value: { } value,
                }
            && (field.Field.IsStatic || field.Instance is IInstanceReferenceOperation)
            && IsDefinitelyNonThrowing(value);
    }

    private static bool CanExceptionBypassCompletedAssignment(
        ISimpleAssignmentOperation assignment,
        IOperation destination
    )
    {
        var semanticModel = assignment.SemanticModel ?? destination.SemanticModel;
        if (semanticModel == null)
            return true;

        if (
            destination
                .Syntax.Ancestors()
                .OfType<FinallyClauseSyntax>()
                .Any(finallyClause =>
                    finallyClause.Parent is TryStatementSyntax tryStatement
                    && tryStatement.Block.Span.Contains(assignment.Syntax.Span)
                )
        )
        {
            return true;
        }

        foreach (
            var containingTry in assignment
                .Syntax.Ancestors()
                .OfType<TryStatementSyntax>()
                .Where(tryStatement => tryStatement.Block.Span.Contains(assignment.Syntax.Span))
        )
        {
            foreach (var catchClause in containingTry.Catches)
            {
                if (catchClause.Block.Span.Contains(destination.Syntax.Span))
                    return true;

                if (containingTry.Span.End >= destination.Syntax.SpanStart)
                    continue;

                var catchFlow = semanticModel.AnalyzeControlFlow(catchClause.Block);
                if (!catchFlow.Succeeded || catchFlow.EndPointIsReachable)
                    return true;
            }

            if (containingTry.Finally?.Block.Span.Contains(destination.Syntax.Span) == true)
                return true;
        }

        return false;
    }

    private static bool IsDefinitelyNonThrowing(IOperation operation)
    {
        while (operation is IParenthesizedOperation parenthesized)
            operation = parenthesized.Operand;

        return operation.ConstantValue.HasValue
            || operation
                is IDefaultValueOperation
                    or INameOfOperation
                    or ITypeOfOperation
                    or ILocalReferenceOperation
                    or IParameterReferenceOperation;
    }

    private static bool CanFlowToSave(
        MutationEvidence mutation,
        SaveEvidence save,
        OperationCollector collector,
        ControlFlowGraph? flowGraph
    )
    {
        if (
            ContainsOperation(mutation.Operation, save.Invocation)
            && !mutation.ContainedSaveContexts.Contains(save.Context)
        )
            return false;

        if (flowGraph == null)
            return save.Position >= mutation.Position;

        var mutationBlock = FindContainingBlock(flowGraph, mutation.Operation);
        var saveBlock = FindContainingBlock(flowGraph, save.Invocation);
        if (mutationBlock == null || saveBlock == null)
            return false;

        var mutationFinally = mutation
            .Operation.Syntax.Ancestors()
            .OfType<FinallyClauseSyntax>()
            .FirstOrDefault();
        var saveFinally = save
            .Invocation.Syntax.Ancestors()
            .OfType<FinallyClauseSyntax>()
            .FirstOrDefault();
        var exceptionalFinallyFlow =
            mutationFinally != null
            && ReferenceEquals(mutationFinally, saveFinally)
            && save.Position >= mutation.Position
            && CanReachIgnoringEntryReachability(mutationBlock, saveBlock);

        if (!CanReach(flowGraph.Blocks[0], mutationBlock) && !exceptionalFinallyFlow)
            return false;

        var normalFlow =
            exceptionalFinallyFlow
            || (
                save.Position >= mutation.Position
                    ? CanReach(mutationBlock, saveBlock)
                    : CanReachAfterLeaving(mutationBlock, saveBlock)
            );
        if (normalFlow && save.Position >= mutation.Position)
        {
            foreach (var predicate in GetRequiredBooleanPredicates(mutation.Operation))
            {
                if (
                    IsBooleanSymbolStableBetween(
                        predicate.Symbol,
                        mutation.Position,
                        save.Position,
                        collector
                    ) && !CanReach(mutationBlock, saveBlock, predicate.Symbol, predicate.Value)
                )
                {
                    normalFlow = false;
                    break;
                }
            }
        }

        return normalFlow || CanFlowThroughCatch(mutation, save, flowGraph);
    }

    private static bool CanFlowThroughCatch(
        MutationEvidence mutation,
        SaveEvidence save,
        ControlFlowGraph flowGraph
    )
    {
        var semanticModel = mutation.Operation.SemanticModel ?? save.Invocation.SemanticModel;
        if (semanticModel == null)
            return false;

        foreach (
            var containingTry in mutation
                .Operation.Syntax.AncestorsAndSelf()
                .OfType<TryStatementSyntax>()
                .Where(tryStatement =>
                    tryStatement.Block.Span.Contains(mutation.Operation.Syntax.Span)
                )
        )
        {
            var containingCatch = save
                .Invocation.Syntax.AncestorsAndSelf()
                .OfType<CatchClauseSyntax>()
                .FirstOrDefault(catchClause => ReferenceEquals(catchClause.Parent, containingTry));
            if (
                containingCatch == null
                || containingCatch.Filter != null
                || HasNestedExecutableBoundary(mutation.Operation.Syntax, containingTry.Block)
                || HasNestedExecutableBoundary(save.Invocation.Syntax, containingCatch.Block)
            )
            {
                continue;
            }

            if (
                CanFlowThroughCatch(
                    mutation,
                    containingTry,
                    containingCatch,
                    semanticModel,
                    flowGraph
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanFlowThroughCatch(
        MutationEvidence mutation,
        TryStatementSyntax containingTry,
        CatchClauseSyntax containingCatch,
        SemanticModel semanticModel,
        ControlFlowGraph flowGraph
    )
    {
        var catchType =
            containingCatch.Declaration == null
                ? null
                : semanticModel.GetTypeInfo(containingCatch.Declaration.Type).Type;
        var mutationBlock = FindContainingBlock(flowGraph, mutation.Operation);
        if (mutationBlock == null)
            return false;

        foreach (
            var throwSyntax in containingTry
                .Block.DescendantNodes(node =>
                    node is not AnonymousFunctionExpressionSyntax
                    && node is not LocalFunctionStatementSyntax
                )
                .Where(node =>
                    node is ThrowStatementSyntax or ThrowExpressionSyntax
                    && node.SpanStart > mutation.Position
                )
        )
        {
            var thrownExpression = throwSyntax switch
            {
                ThrowStatementSyntax throwStatement => throwStatement.Expression,
                ThrowExpressionSyntax throwExpression => throwExpression.Expression,
                _ => null,
            };
            if (thrownExpression == null)
                continue;

            var exceptionType = semanticModel.GetTypeInfo(thrownExpression).Type;
            var throwBlock = FindThrowBlock(flowGraph, throwSyntax);
            if (
                throwBlock == null
                || (
                    !ReferenceEquals(mutationBlock, throwBlock)
                    && !CanReach(mutationBlock, throwBlock)
                )
                || !CatchHandles(containingCatch.Declaration == null, catchType, exceptionType)
                || !CanExceptionReachEnclosingTry(
                    throwSyntax,
                    containingTry,
                    exceptionType,
                    semanticModel,
                    isUnknownException: false,
                    outerCatchesAll: containingCatch.Declaration == null,
                    outerCatchType: catchType
                )
            )
            {
                continue;
            }

            return true;
        }

        foreach (
            var potentiallyThrowingSyntax in containingTry
                .Block.DescendantNodes(node =>
                    node is not AnonymousFunctionExpressionSyntax
                    && node is not LocalFunctionStatementSyntax
                )
                .Where(node =>
                    node.SpanStart > mutation.Operation.Syntax.Span.End
                    && node
                        is InvocationExpressionSyntax
                            or ObjectCreationExpressionSyntax
                            or AwaitExpressionSyntax
                            or ElementAccessExpressionSyntax
                )
        )
        {
            if (
                potentiallyThrowingSyntax
                    .Ancestors()
                    .Any(ancestor => ancestor is ThrowStatementSyntax or ThrowExpressionSyntax)
            )
            {
                continue;
            }

            var potentiallyThrowingOperation = semanticModel.GetOperation(
                potentiallyThrowingSyntax
            );
            if (
                potentiallyThrowingOperation == null
                || !IsPotentiallyThrowing(potentiallyThrowingOperation)
                || !OperationCanReach(mutation.Operation, potentiallyThrowingOperation, flowGraph)
                || !CatchCanHandleUnknownException(containingCatch.Declaration == null, catchType)
                || !CanExceptionReachEnclosingTry(
                    potentiallyThrowingSyntax,
                    containingTry,
                    exceptionType: null,
                    semanticModel,
                    isUnknownException: true,
                    outerCatchesAll: containingCatch.Declaration == null,
                    outerCatchType: catchType
                )
            )
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool CanExceptionReachEnclosingTry(
        SyntaxNode source,
        TryStatementSyntax containingTry,
        ITypeSymbol? exceptionType,
        SemanticModel semanticModel,
        bool isUnknownException,
        bool outerCatchesAll,
        ITypeSymbol? outerCatchType
    )
    {
        foreach (var nestedTry in source.Ancestors().OfType<TryStatementSyntax>())
        {
            if (ReferenceEquals(nestedTry, containingTry))
                return true;
            if (!nestedTry.Block.Span.Contains(source.Span))
                continue;

            var isDefinitelyIntercepted = false;
            foreach (var nestedCatch in nestedTry.Catches)
            {
                var nestedCatchType =
                    nestedCatch.Declaration == null
                        ? null
                        : semanticModel.GetTypeInfo(nestedCatch.Declaration.Type).Type;
                var handles = isUnknownException
                    ? CatchCanHandleUnknownException(
                        nestedCatch.Declaration == null,
                        nestedCatchType
                    )
                    : CatchHandles(nestedCatch.Declaration == null, nestedCatchType, exceptionType);
                if (!handles)
                    continue;

                var filterValue =
                    nestedCatch.Filter == null
                        ? true
                        : semanticModel.GetConstantValue(nestedCatch.Filter.FilterExpression).Value
                            as bool?;
                if (filterValue == false)
                    continue;
                if (
                    CatchDefinitelyPropagatesException(
                        nestedCatch,
                        semanticModel,
                        outerCatchesAll,
                        outerCatchType
                    )
                )
                {
                    break;
                }

                if (filterValue == true)
                {
                    isDefinitelyIntercepted = true;
                    break;
                }
            }

            if (isDefinitelyIntercepted)
                return false;
        }

        return false;
    }

    private static bool CatchDefinitelyPropagatesException(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        bool outerCatchesAll,
        ITypeSymbol? outerCatchType
    )
    {
        var catchFlow = semanticModel.AnalyzeControlFlow(catchClause.Block);
        if (
            !catchFlow.Succeeded
            || catchFlow.EndPointIsReachable
            || catchFlow.ExitPoints.Length != 0
            || catchClause.Block.Statements.LastOrDefault()
                is not ThrowStatementSyntax throwStatement
        )
        {
            return false;
        }

        if (throwStatement.Expression == null)
            return true;

        var caughtException =
            catchClause.Declaration == null
                ? null
                : semanticModel.GetDeclaredSymbol(catchClause.Declaration);
        if (
            caughtException != null
            && SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(throwStatement.Expression).Symbol,
                caughtException
            )
        )
        {
            return true;
        }

        var propagatedType = semanticModel.GetTypeInfo(throwStatement.Expression).Type;
        return propagatedType != null
            && CatchHandles(outerCatchesAll, outerCatchType, propagatedType);
    }

    private static bool HasNestedExecutableBoundary(SyntaxNode syntax, SyntaxNode boundary)
    {
        for (var current = syntax.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, boundary))
                return false;
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return true;
        }

        return true;
    }

    private static bool CatchHandles(
        bool catchesAll,
        ITypeSymbol? catchType,
        ITypeSymbol? exceptionType
    )
    {
        if (catchesAll)
            return true;
        if (catchType == null || exceptionType == null)
            return false;

        for (
            var current = exceptionType as INamedTypeSymbol;
            current != null;
            current = current.BaseType
        )
        {
            if (SymbolEqualityComparer.Default.Equals(current, catchType))
                return true;
        }

        return exceptionType.AllInterfaces.Any(interfaceType =>
            SymbolEqualityComparer.Default.Equals(interfaceType, catchType)
        );
    }

    private static bool IsPotentiallyThrowing(IOperation operation)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation
                is IInvocationOperation
                    or IObjectCreationOperation
                    or IAwaitOperation
                    or IArrayElementReferenceOperation
            || operation is IPropertyReferenceOperation { Property.IsIndexer: true };
    }

    private static bool CatchCanHandleUnknownException(bool catchesAll, ITypeSymbol? catchType)
    {
        if (catchesAll)
            return true;

        for (
            var current = catchType as INamedTypeSymbol;
            current != null;
            current = current.BaseType
        )
        {
            if (
                current.Name == "Exception"
                && current.ContainingNamespace?.ToDisplayString() == "System"
            )
            {
                return true;
            }
        }

        return false;
    }

    private static ControlFlowGraph? TryCreateFlowGraph(
        IOperation? executableRoot,
        CancellationToken cancellationToken
    )
    {
        if (executableRoot == null)
            return null;

        try
        {
            return executableRoot switch
            {
                IMethodBodyOperation methodBody when methodBody.Parent == null =>
                    ControlFlowGraph.Create(methodBody, cancellationToken),
                IConstructorBodyOperation constructorBody when constructorBody.Parent == null =>
                    ControlFlowGraph.Create(constructorBody, cancellationToken),
                _ when executableRoot.SemanticModel != null => ControlFlowGraph.Create(
                    executableRoot.Syntax,
                    executableRoot.SemanticModel,
                    cancellationToken
                ),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static BasicBlock? FindContainingBlock(ControlFlowGraph graph, IOperation operation)
    {
        foreach (var block in graph.Blocks)
        {
            foreach (var root in block.Operations)
            {
                if (ContainsOperation(root, operation))
                    return block;
            }

            if (block.BranchValue != null && ContainsOperation(block.BranchValue, operation))
                return block;
        }

        return null;
    }

    private static BasicBlock? FindThrowBlock(ControlFlowGraph graph, SyntaxNode throwSyntax)
    {
        foreach (var block in graph.Blocks)
        {
            if (
                block.BranchValue != null
                && block.BranchValue.Syntax.SyntaxTree == throwSyntax.SyntaxTree
                && throwSyntax.Span.Contains(block.BranchValue.Syntax.Span)
            )
            {
                return block;
            }

            foreach (var operation in block.Operations)
            {
                if (
                    operation.Syntax.SyntaxTree == throwSyntax.SyntaxTree
                    && throwSyntax.Span.Contains(operation.Syntax.Span)
                )
                {
                    return block;
                }
            }
        }

        return null;
    }

    private static bool ContainsOperation(IOperation root, IOperation target)
    {
        if (ReferenceEquals(root, target))
            return true;

        if (
            root.Syntax.SyntaxTree == target.Syntax.SyntaxTree
            && root.Syntax.Span.Contains(target.Syntax.Span)
        )
        {
            return true;
        }

        foreach (var child in root.ChildOperations)
        {
            if (ContainsOperation(child, target))
                return true;
        }

        return false;
    }

    private static bool CanReach(
        BasicBlock start,
        BasicBlock target,
        ISymbol assumedSymbol,
        bool assumedValue
    )
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!current.IsReachable || !visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending, assumedSymbol, assumedValue);
        }

        return false;
    }

    private static bool CanReachAfterLeaving(
        BasicBlock start,
        BasicBlock target,
        ISymbol assumedSymbol,
        bool assumedValue
    )
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        EnqueueFeasibleSuccessors(start, pending, assumedSymbol, assumedValue);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!current.IsReachable || !visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending, assumedSymbol, assumedValue);
        }

        return false;
    }

    private static void EnqueueFeasibleSuccessors(
        BasicBlock block,
        Queue<BasicBlock> pending,
        ISymbol assumedSymbol,
        bool assumedValue
    )
    {
        if (TryEvaluateCondition(block.BranchValue, assumedSymbol, assumedValue, out var condition))
        {
            var takeConditional = block.ConditionKind switch
            {
                ControlFlowConditionKind.WhenTrue => condition,
                ControlFlowConditionKind.WhenFalse => !condition,
                _ => (bool?)null,
            };
            if (takeConditional.HasValue)
            {
                var destination = (
                    takeConditional.Value ? block.ConditionalSuccessor : block.FallThroughSuccessor
                )?.Destination;
                if (destination != null)
                    pending.Enqueue(destination);
                return;
            }
        }

        var fallThrough = block.FallThroughSuccessor?.Destination;
        if (fallThrough != null)
            pending.Enqueue(fallThrough);
        var conditional = block.ConditionalSuccessor?.Destination;
        if (conditional != null)
            pending.Enqueue(conditional);
    }

    private static bool TryEvaluateCondition(
        IOperation? operation,
        ISymbol assumedSymbol,
        bool assumedValue,
        out bool value
    )
    {
        if (operation?.ConstantValue is { HasValue: true, Value: bool constant })
        {
            value = constant;
            return true;
        }
        if (operation == null)
        {
            value = false;
            return false;
        }

        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation is IParameterReferenceOperation parameter
                && SymbolEqualityComparer.Default.Equals(parameter.Parameter, assumedSymbol)
            || operation is ILocalReferenceOperation local
                && SymbolEqualityComparer.Default.Equals(local.Local, assumedSymbol)
        )
        {
            value = assumedValue;
            return true;
        }

        if (
            operation
                is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: var operand }
            && TryEvaluateCondition(operand, assumedSymbol, assumedValue, out var operandValue)
        )
        {
            value = !operandValue;
            return true;
        }

        if (
            operation
                is IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals,
                    LeftOperand: var left,
                    RightOperand: var right,
                } binary
            && TryEvaluateCondition(left, assumedSymbol, assumedValue, out var leftValue)
            && TryEvaluateCondition(right, assumedSymbol, assumedValue, out var rightValue)
        )
        {
            value =
                binary.OperatorKind == BinaryOperatorKind.Equals
                    ? leftValue == rightValue
                    : leftValue != rightValue;
            return true;
        }

        value = false;
        return false;
    }

    private static bool CanReachWithNullState(
        BasicBlock start,
        BasicBlock target,
        ISymbol assumedSymbol,
        bool assumedNull
    )
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!current.IsReachable || !visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleNullStateSuccessors(current, pending, assumedSymbol, assumedNull);
        }

        return false;
    }

    private static void EnqueueFeasibleNullStateSuccessors(
        BasicBlock block,
        Queue<BasicBlock> pending,
        ISymbol assumedSymbol,
        bool assumedNull
    )
    {
        if (
            TryEvaluateNullCondition(
                block.BranchValue,
                assumedSymbol,
                assumedNull,
                out var condition
            )
        )
        {
            var takeConditional = block.ConditionKind switch
            {
                ControlFlowConditionKind.WhenTrue => condition,
                ControlFlowConditionKind.WhenFalse => !condition,
                _ => (bool?)null,
            };
            if (takeConditional.HasValue)
            {
                var destination = (
                    takeConditional.Value ? block.ConditionalSuccessor : block.FallThroughSuccessor
                )?.Destination;
                if (destination != null)
                    pending.Enqueue(destination);
                return;
            }
        }

        var fallThrough = block.FallThroughSuccessor?.Destination;
        if (fallThrough != null)
            pending.Enqueue(fallThrough);
        var conditional = block.ConditionalSuccessor?.Destination;
        if (conditional != null)
            pending.Enqueue(conditional);
    }

    private static bool TryEvaluateNullCondition(
        IOperation? operation,
        ISymbol assumedSymbol,
        bool assumedNull,
        out bool value
    )
    {
        if (operation?.ConstantValue is { HasValue: true, Value: bool constant })
        {
            value = constant;
            return true;
        }
        if (operation == null)
        {
            value = false;
            return false;
        }

        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation
                is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: var operand }
            && TryEvaluateNullCondition(operand, assumedSymbol, assumedNull, out var operandValue)
        )
        {
            value = !operandValue;
            return true;
        }

        if (
            operation
                is IBinaryOperation
                {
                    OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals,
                    LeftOperand: var left,
                    RightOperand: var right,
                } binary
            && TryEvaluateNullOperand(left, assumedSymbol, assumedNull, out var leftIsNull)
            && TryEvaluateNullOperand(right, assumedSymbol, assumedNull, out var rightIsNull)
        )
        {
            value =
                binary.OperatorKind == BinaryOperatorKind.Equals
                    ? leftIsNull == rightIsNull
                    : leftIsNull != rightIsNull;
            return true;
        }

        if (operation is IIsPatternOperation isPattern)
        {
            var negated = false;
            var pattern = isPattern.Pattern;
            if (pattern is INegatedPatternOperation negatedPattern)
            {
                negated = true;
                pattern = negatedPattern.Pattern;
            }

            if (
                pattern
                    is IConstantPatternOperation
                    {
                        Value.ConstantValue: { HasValue: true, Value: null },
                    }
                && TryEvaluateNullOperand(
                    isPattern.Value,
                    assumedSymbol,
                    assumedNull,
                    out var inputIsNull
                )
            )
            {
                value = negated ? !inputIsNull : inputIsNull;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool TryEvaluateNullOperand(
        IOperation operation,
        ISymbol assumedSymbol,
        bool assumedNull,
        out bool isNull
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (operation.ConstantValue is { HasValue: true, Value: null })
        {
            isNull = true;
            return true;
        }
        if (
            operation is IParameterReferenceOperation parameter
                && SymbolEqualityComparer.Default.Equals(parameter.Parameter, assumedSymbol)
            || operation is ILocalReferenceOperation local
                && SymbolEqualityComparer.Default.Equals(local.Local, assumedSymbol)
        )
        {
            isNull = assumedNull;
            return true;
        }
        if (operation is IObjectCreationOperation)
        {
            isNull = false;
            return true;
        }

        isNull = false;
        return false;
    }

    private static bool CanReach(BasicBlock start, BasicBlock target)
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!current.IsReachable || !visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending);
        }

        return false;
    }

    private static bool CanReachIgnoringEntryReachability(BasicBlock start, BasicBlock target)
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending);
        }

        return false;
    }

    private static bool CanReachAfterLeaving(BasicBlock start, BasicBlock target)
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        EnqueueFeasibleSuccessors(start, pending);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!current.IsReachable || !visited.Add(current.Ordinal))
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending);
        }

        return false;
    }

    private static bool CanReachAfterLeavingAvoiding(
        BasicBlock start,
        BasicBlock target,
        BasicBlock excluded
    )
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        EnqueueFeasibleSuccessors(start, pending);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (
                !current.IsReachable
                || ReferenceEquals(current, excluded)
                || !visited.Add(current.Ordinal)
            )
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending);
        }

        return false;
    }

    private static bool CanReachAvoiding(BasicBlock start, BasicBlock target, BasicBlock excluded)
    {
        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (
                !current.IsReachable
                || ReferenceEquals(current, excluded)
                || !visited.Add(current.Ordinal)
            )
                continue;
            if (ReferenceEquals(current, target))
                return true;

            EnqueueFeasibleSuccessors(current, pending);
        }

        return false;
    }

    private static void EnqueueFeasibleSuccessors(BasicBlock block, Queue<BasicBlock> pending)
    {
        if (block.BranchValue?.ConstantValue is { HasValue: true, Value: bool condition })
        {
            var takeConditional = block.ConditionKind switch
            {
                ControlFlowConditionKind.WhenTrue => condition,
                ControlFlowConditionKind.WhenFalse => !condition,
                _ => (bool?)null,
            };
            if (takeConditional.HasValue)
            {
                var destination = (
                    takeConditional.Value ? block.ConditionalSuccessor : block.FallThroughSuccessor
                )?.Destination;
                if (destination != null)
                    pending.Enqueue(destination);
                return;
            }
        }

        var fallThrough = block.FallThroughSuccessor?.Destination;
        if (fallThrough != null)
            pending.Enqueue(fallThrough);
        var conditional = block.ConditionalSuccessor?.Destination;
        if (conditional != null)
            pending.Enqueue(conditional);
    }

    private sealed class QuerySource
    {
        internal QuerySource(
            ISymbol context,
            INamedTypeSymbol entityType,
            bool isTracked,
            bool? explicitTracking = null,
            IPropertyReferenceOperation? dbSetOrigin = null,
            ISymbol? contextAccess = null
        )
        {
            Context = context;
            EntityType = entityType;
            IsTracked = isTracked;
            ExplicitTracking = explicitTracking;
            DbSetOrigin = dbSetOrigin;
            ContextAccess = contextAccess ?? context;
        }

        internal ISymbol Context { get; }
        internal ISymbol ContextAccess { get; }
        internal INamedTypeSymbol EntityType { get; }
        internal bool IsTracked { get; }
        internal bool? ExplicitTracking { get; }
        internal IPropertyReferenceOperation? DbSetOrigin { get; }

        internal QuerySource WithTracking(bool isTracked) =>
            new(
                Context,
                EntityType,
                isTracked,
                explicitTracking: isTracked,
                dbSetOrigin: DbSetOrigin,
                contextAccess: ContextAccess
            );
    }

    private sealed class EntitySource
    {
        internal EntitySource(
            ISymbol context,
            INamedTypeSymbol entityType,
            bool isTracked,
            bool honorsContextTrackingBehavior,
            int materializationPosition,
            IInvocationOperation materialization,
            ISymbol? contextAccess = null
        )
        {
            Context = context;
            EntityType = entityType;
            IsTracked = isTracked;
            HonorsContextTrackingBehavior = honorsContextTrackingBehavior;
            MaterializationPosition = materializationPosition;
            Materialization = materialization;
            ContextAccess = contextAccess ?? context;
        }

        internal ISymbol Context { get; }
        internal ISymbol ContextAccess { get; }
        internal INamedTypeSymbol EntityType { get; }
        internal bool IsTracked { get; set; }
        internal bool HonorsContextTrackingBehavior { get; }
        internal ImmutableArray<ISimpleAssignmentOperation> TrackingPromotions { get; set; }
        internal int MaterializationPosition { get; }
        internal IInvocationOperation Materialization { get; }
    }

    private readonly struct BooleanPredicate
    {
        internal BooleanPredicate(ISymbol symbol, bool value)
        {
            Symbol = symbol;
            Value = value;
        }

        internal ISymbol Symbol { get; }
        internal bool Value { get; }
    }

    private enum TransactionArgumentNullState
    {
        Unknown,
        Null,
        NonNull,
    }

    private readonly struct MutationEvidence
    {
        internal MutationEvidence(
            EntitySource entity,
            IPropertySymbol property,
            Location location,
            int position,
            IOperation operation,
            ImmutableHashSet<ISymbol>? containedSaveContexts = null,
            bool isPlainSelfAssignment = false
        )
        {
            Entity = entity;
            Property = property;
            Location = location;
            Position = position;
            Operation = operation;
            IsPlainSelfAssignment = isPlainSelfAssignment;
            ContainedSaveContexts =
                containedSaveContexts
                ?? ImmutableHashSet<ISymbol>.Empty.WithComparer(SymbolEqualityComparer.Default);
        }

        internal EntitySource Entity { get; }
        internal IPropertySymbol Property { get; }
        internal Location Location { get; }
        internal int Position { get; }
        internal IOperation Operation { get; }
        internal bool IsPlainSelfAssignment { get; }
        internal ImmutableHashSet<ISymbol> ContainedSaveContexts { get; }
    }

    private readonly struct SaveEvidence
    {
        internal SaveEvidence(
            ISymbol context,
            Location location,
            int position,
            IInvocationOperation invocation,
            int? containedPosition = null,
            ISymbol? contextAccess = null
        )
        {
            Context = context;
            Location = location;
            Position = position;
            Invocation = invocation;
            ContainedPosition = containedPosition;
            ContextAccess = contextAccess ?? context;
        }

        internal ISymbol Context { get; }
        internal ISymbol ContextAccess { get; }
        internal Location Location { get; }
        internal int Position { get; }
        internal IInvocationOperation Invocation { get; }
        internal int? ContainedPosition { get; }
    }

    private readonly struct TransactionResetEvidence
    {
        internal TransactionResetEvidence(ISymbol context, IInvocationOperation invocation)
        {
            Context = context;
            Invocation = invocation;
        }

        internal ISymbol Context { get; }
        internal IInvocationOperation Invocation { get; }
    }

    private readonly struct TransactionEvidence
    {
        internal TransactionEvidence(
            ISymbol context,
            int position,
            IInvocationOperation invocation,
            ImmutableArray<int> protectedSavePositions = default,
            IOperation? condition = null,
            bool conditionValue = false
        )
        {
            Context = context;
            Position = position;
            Invocation = invocation;
            ProtectedSavePositions = protectedSavePositions.IsDefault
                ? ImmutableArray<int>.Empty
                : protectedSavePositions;
            Condition = condition;
            ConditionValue = conditionValue;
        }

        internal ISymbol Context { get; }
        internal int Position { get; }
        internal IInvocationOperation Invocation { get; }
        internal ImmutableArray<int> ProtectedSavePositions { get; }
        internal IOperation? Condition { get; }
        internal bool ConditionValue { get; }
    }
}

internal static class LostUpdateOperationFacts
{
    private static readonly ImmutableHashSet<string> SingleEntityTerminals =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "First",
            "FirstOrDefault",
            "Single",
            "SingleOrDefault",
            "Last",
            "LastOrDefault",
            "FirstAsync",
            "FirstOrDefaultAsync",
            "SingleAsync",
            "SingleOrDefaultAsync",
            "LastAsync",
            "LastOrDefaultAsync",
            "Find",
            "FindAsync"
        );

    internal static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IAwaitOperation awaitOperation:
                    operation = awaitOperation.Operation;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    internal static bool IsSingleEntityTerminal(IMethodSymbol method)
    {
        if (!SingleEntityTerminals.Contains(method.Name))
            return false;

        if (method.Name is "Find" or "FindAsync")
            return IsEfFindMethod(method);

        if (method.Name.EndsWith("Async", StringComparison.Ordinal))
            return IsEfAsyncSingleEntityTerminal(method);

        var definition = method.ReducedFrom ?? method;
        var namespaceName = definition.ContainingNamespace?.ToDisplayString();
        return namespaceName == "System.Linq";
    }

    private static bool IsEfFindMethod(IMethodSymbol method)
    {
        if (
            !TryGetFindEntityType(method, out var entityType)
            || !HasEfFindSignature(method, entityType)
        )
            return false;

        for (
            IMethodSymbol? current = method.ConstructedFrom;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (
                TryGetDeclaredEfFindEntityType(current, out var declaredEntityType)
                && HasEfFindSignature(current, declaredEntityType)
            )
            {
                return true;
            }

            if (!current.IsOverride || IsExplicitlyNewMethod(current))
                break;
        }

        return false;
    }

    private static bool IsExplicitlyNewMethod(IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is MethodDeclarationSyntax declaration
            && declaration.Modifiers.Any(modifier =>
                modifier.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.NewKeyword
            )
        );
    }

    private static bool TryGetFindEntityType(IMethodSymbol method, out ITypeSymbol entityType)
    {
        if (IsDbContextType(method.ContainingType))
        {
            if (method.Arity == 1 && method.TypeArguments.Length == 1)
            {
                entityType = method.TypeArguments[0];
                return true;
            }
        }
        else if (
            method.Arity == 0
            && TryGetDbSetEntityType(method.ContainingType, out var setEntityType)
        )
        {
            entityType = setEntityType;
            return true;
        }

        entityType = null!;
        return false;
    }

    private static bool TryGetDeclaredEfFindEntityType(
        IMethodSymbol method,
        out ITypeSymbol entityType
    )
    {
        var containingDefinition = method.ContainingType.OriginalDefinition;
        if (IsEfType(containingDefinition, "DbContext") && containingDefinition.Arity == 0)
        {
            if (method.Arity == 1 && method.TypeArguments.Length == 1)
            {
                entityType = method.TypeArguments[0];
                return true;
            }
        }
        else if (
            IsEfType(containingDefinition, "DbSet")
            && containingDefinition.Arity == 1
            && method.Arity == 0
            && method.ContainingType.TypeArguments.Length == 1
        )
        {
            entityType = method.ContainingType.TypeArguments[0];
            return true;
        }

        entityType = null!;
        return false;
    }

    private static bool HasEfFindSignature(IMethodSymbol method, ITypeSymbol entityType)
    {
        if (
            method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Parameters.Length is < 1 or > 2
            || !IsObjectArrayParameter(method.Parameters[0])
        )
        {
            return false;
        }

        if (method.Name == "Find")
        {
            return method.Parameters.Length == 1
                && IsEntityTypeForMethod(method.ReturnType, entityType, method);
        }

        return method.Name == "FindAsync"
            && (
                method.Parameters.Length == 1
                || method.Parameters.Length == 2
                    && IsCancellationTokenParameter(method.Parameters[1])
            )
            && IsFrameworkGenericEntityReturn(method.ReturnType, "ValueTask", entityType, method);
    }

    private static bool IsEfAsyncSingleEntityTerminal(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        if (
            !definition.IsStatic
            || !definition.IsExtensionMethod
            || definition.Arity != 1
            || definition.TypeArguments.Length != 1
            || definition.ContainingType.OriginalDefinition
                is not { Name: "EntityFrameworkQueryableExtensions", Arity: 0 } containingType
            || containingType.ContainingNamespace?.ToDisplayString()
                != "Microsoft.EntityFrameworkCore"
            || definition.Parameters.Length is < 2 or > 3
            || !IsQueryableOf(definition.Parameters[0].Type, definition.TypeArguments[0])
            || !IsCancellationTokenParameter(
                definition.Parameters[definition.Parameters.Length - 1]
            )
            || !IsFrameworkGenericReturn(definition.ReturnType, "Task", definition.TypeArguments[0])
        )
        {
            return false;
        }

        return definition.Parameters.Length == 2
            || IsEntityPredicateParameter(definition.Parameters[1], definition.TypeArguments[0]);
    }

    private static bool IsQueryableOf(ITypeSymbol type, ITypeSymbol entityType)
    {
        return type
                is INamedTypeSymbol
                {
                    Name: "IQueryable",
                    Arity: 1,
                    TypeArguments.Length: 1,
                } queryable
            && queryable.ContainingNamespace?.ToDisplayString() == "System.Linq"
            && SymbolEqualityComparer.Default.Equals(queryable.TypeArguments[0], entityType);
    }

    private static bool IsEntityPredicateParameter(
        IParameterSymbol parameter,
        ITypeSymbol entityType
    )
    {
        if (
            parameter.RefKind != RefKind.None
            || parameter.Type is not INamedTypeSymbol expressionType
            || expressionType.Name != "Expression"
            || expressionType.Arity != 1
            || expressionType.TypeArguments.Length != 1
            || expressionType.ContainingNamespace?.ToDisplayString() != "System.Linq.Expressions"
            || expressionType.TypeArguments[0] is not INamedTypeSymbol functionType
            || functionType.Name != "Func"
            || functionType.Arity != 2
            || functionType.TypeArguments.Length != 2
            || functionType.ContainingNamespace?.ToDisplayString() != "System"
        )
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(functionType.TypeArguments[0], entityType)
            && functionType.TypeArguments[1].SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsObjectArrayParameter(IParameterSymbol parameter)
    {
        return parameter.RefKind == RefKind.None
            && parameter.Type
                is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Object };
    }

    private static bool IsFrameworkGenericReturn(
        ITypeSymbol returnType,
        string typeName,
        ITypeSymbol resultType
    )
    {
        return returnType is INamedTypeSymbol { Arity: 1, TypeArguments.Length: 1 } awaitable
            && awaitable.Name == typeName
            && awaitable.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && SymbolEqualityComparer.Default.Equals(awaitable.TypeArguments[0], resultType);
    }

    internal static bool IsFrameworkConfigureAwait(IMethodSymbol method)
    {
        if (
            method.Name != "ConfigureAwait"
            || method.IsStatic
            || method.Parameters.Length != 1
            || method.ContainingType is not { } containingType
            || method.ReturnType is not INamedTypeSymbol returnType
        )
        {
            return false;
        }

        var containingDefinition = containingType.OriginalDefinition;
        var returnDefinition = returnType.OriginalDefinition;
        if (
            containingDefinition.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks"
            || returnDefinition.ContainingNamespace.ToDisplayString()
                != "System.Runtime.CompilerServices"
        )
        {
            return false;
        }

        return containingDefinition.Name switch
        {
            "Task" => returnDefinition.Name == "ConfiguredTaskAwaitable"
                && containingDefinition.Arity == returnDefinition.Arity,
            "ValueTask" => returnDefinition.Name == "ConfiguredValueTaskAwaitable"
                && containingDefinition.Arity == returnDefinition.Arity,
            _ => false,
        };
    }

    internal static bool IsFrameworkTaskResultProperty(IPropertySymbol property)
    {
        if (
            property.Name != "Result"
            || property.IsStatic
            || property.Parameters.Length != 0
            || property.ContainingType is not { } containingType
        )
        {
            return false;
        }

        var definition = containingType.OriginalDefinition;
        return definition.Arity == 1
            && definition.Name is "Task" or "ValueTask"
            && definition.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
    }

    internal static bool IsFrameworkBlockingGetResult(
        IMethodSymbol getAwaiter,
        IMethodSymbol getResult
    )
    {
        if (
            getAwaiter.Name != "GetAwaiter"
            || getAwaiter.IsStatic
            || getAwaiter.Arity != 0
            || getAwaiter.Parameters.Length != 0
            || getResult.Name != "GetResult"
            || getResult.IsStatic
            || getResult.Arity != 0
            || getResult.Parameters.Length != 0
            || !SymbolEqualityComparer.Default.Equals(
                getAwaiter.ReturnType,
                getResult.ContainingType
            )
        )
        {
            return false;
        }

        var awaitable = getAwaiter.ContainingType.OriginalDefinition;
        var awaitableNamespace = awaitable.ContainingNamespace.ToDisplayString();
        if (awaitableNamespace == "System.Threading.Tasks")
        {
            if (awaitable.Arity != 1 || awaitable.Name is not ("Task" or "ValueTask"))
                return false;
        }
        else if (awaitableNamespace == "System.Runtime.CompilerServices")
        {
            if (
                awaitable.Arity != 1
                || awaitable.Name
                    is not ("ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable")
            )
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        var awaiter = getResult.ContainingType;
        if (awaiter.ContainingNamespace.ToDisplayString() != "System.Runtime.CompilerServices")
        {
            return false;
        }

        return awaiter.Name
            is "TaskAwaiter"
                or "ValueTaskAwaiter"
                or "ConfiguredTaskAwaiter"
                or "ConfiguredValueTaskAwaiter";
    }

    internal static bool IsTrackingOperation(IMethodSymbol method)
    {
        return method.Name is "Update" or "UpdateRange" or "Attach" or "AttachRange"
            && IsEfStateChangingMethod(method);
    }

    internal static bool PersistsPriorMutation(IMethodSymbol method)
    {
        return method.Name is "Update" or "UpdateRange" && IsEfStateChangingMethod(method);
    }

    internal static bool IsRemovalOperation(IMethodSymbol method)
    {
        return method.Name is "Remove" or "RemoveRange" && IsEfStateChangingMethod(method);
    }

    private static bool IsEfStateChangingMethod(IMethodSymbol method)
    {
        if (!TryGetStateChangingEntityType(method, out var entityType))
            return false;

        for (
            IMethodSymbol? current = method.ConstructedFrom;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (
                TryGetDeclaredEfStateChangingEntityType(current, out var declaredEntityType)
                && HasEfStateChangingSignature(current, declaredEntityType)
            )
            {
                return true;
            }

            if (!current.IsOverride || IsExplicitlyNewMethod(current))
                break;
        }

        return false;
    }

    private static bool TryGetStateChangingEntityType(
        IMethodSymbol method,
        out ITypeSymbol entityType
    )
    {
        if (IsDbContextType(method.ContainingType))
        {
            if (
                method.Name.EndsWith("Range", StringComparison.Ordinal)
                && method.Arity == 0
                && TryGetContextRangeEntityType(method, out entityType)
            )
            {
                return HasEfStateChangingSignature(method, entityType);
            }

            if (
                method.Arity == 1
                && method.TypeArguments.Length == 1
                && HasEfStateChangingSignature(method, method.TypeArguments[0])
            )
            {
                entityType = method.TypeArguments[0];
                return true;
            }
        }
        else if (
            method.Arity == 0
            && TryGetDbSetEntityType(method.ContainingType, out var setEntityType)
            && HasEfStateChangingSignature(method, setEntityType)
        )
        {
            entityType = setEntityType;
            return true;
        }

        entityType = null!;
        return false;
    }

    private static bool TryGetDeclaredEfStateChangingEntityType(
        IMethodSymbol method,
        out ITypeSymbol entityType
    )
    {
        var containingDefinition = method.ContainingType.OriginalDefinition;
        if (IsEfType(containingDefinition, "DbContext") && containingDefinition.Arity == 0)
        {
            if (
                method.Name.EndsWith("Range", StringComparison.Ordinal)
                && method.Arity == 0
                && TryGetContextRangeEntityType(method, out entityType)
            )
            {
                return true;
            }

            if (method.Arity == 1 && method.TypeArguments.Length == 1)
            {
                entityType = method.TypeArguments[0];
                return true;
            }
        }
        else if (
            IsEfType(containingDefinition, "DbSet")
            && containingDefinition.Arity == 1
            && method.Arity == 0
            && method.ContainingType.TypeArguments.Length == 1
        )
        {
            entityType = method.ContainingType.TypeArguments[0];
            return true;
        }

        entityType = null!;
        return false;
    }

    private static bool TryGetContextRangeEntityType(
        IMethodSymbol method,
        out ITypeSymbol entityType
    )
    {
        if (method.Parameters.Length == 1)
        {
            var parameterType = method.Parameters[0].Type;
            if (
                parameterType is IArrayTypeSymbol
                {
                    Rank: 1,
                    ElementType.SpecialType: SpecialType.System_Object,
                } array
            )
            {
                entityType = array.ElementType;
                return true;
            }

            if (
                parameterType
                    is INamedTypeSymbol
                    {
                        Name: "IEnumerable",
                        Arity: 1,
                        TypeArguments.Length: 1,
                    } enumerable
                && enumerable.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
                && enumerable.TypeArguments[0].SpecialType == SpecialType.System_Object
            )
            {
                entityType = enumerable.TypeArguments[0];
                return true;
            }
        }

        entityType = null!;
        return false;
    }

    private static bool HasEfStateChangingSignature(IMethodSymbol method, ITypeSymbol entityType)
    {
        if (
            method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Parameters.Length != 1
            || method.Parameters[0].RefKind != RefKind.None
        )
        {
            return false;
        }

        if (method.Name.EndsWith("Range", StringComparison.Ordinal))
        {
            return method.ReturnsVoid
                && (
                    IsEntityArray(method.Parameters[0].Type, entityType)
                    || IsEnumerableOf(method.Parameters[0].Type, entityType)
                );
        }

        return IsEntityTypeForMethod(method.Parameters[0].Type, entityType, method)
            && IsEntityEntryOf(method.ReturnType, entityType, method);
    }

    private static bool IsFrameworkGenericEntityReturn(
        ITypeSymbol returnType,
        string typeName,
        ITypeSymbol entityType,
        IMethodSymbol method
    )
    {
        return returnType is INamedTypeSymbol { Arity: 1, TypeArguments.Length: 1 } awaitable
            && awaitable.Name == typeName
            && awaitable.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && IsEntityTypeForMethod(awaitable.TypeArguments[0], entityType, method);
    }

    private static bool IsEntityTypeForMethod(
        ITypeSymbol actualType,
        ITypeSymbol entityType,
        IMethodSymbol method
    )
    {
        return SymbolEqualityComparer.Default.Equals(actualType, entityType)
            || method.Arity == 1
                && actualType
                    is ITypeParameterSymbol
                    {
                        TypeParameterKind: TypeParameterKind.Method,
                        Ordinal: 0,
                    }
                && (
                    SymbolEqualityComparer.Default.Equals(entityType, method.TypeArguments[0])
                    || entityType
                        is ITypeParameterSymbol
                        {
                            TypeParameterKind: TypeParameterKind.Method,
                            Ordinal: 0,
                        }
                );
    }

    private static bool IsEntityArray(ITypeSymbol type, ITypeSymbol entityType)
    {
        return type is IArrayTypeSymbol { Rank: 1 } array
            && SymbolEqualityComparer.Default.Equals(array.ElementType, entityType);
    }

    private static bool IsEnumerableOf(ITypeSymbol type, ITypeSymbol entityType)
    {
        return type is INamedTypeSymbol { Arity: 1, TypeArguments.Length: 1 } enumerable
            && enumerable.Name == "IEnumerable"
            && enumerable.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
            && SymbolEqualityComparer.Default.Equals(enumerable.TypeArguments[0], entityType);
    }

    private static bool IsEntityEntryOf(
        ITypeSymbol type,
        ITypeSymbol entityType,
        IMethodSymbol method
    )
    {
        return type is INamedTypeSymbol { Arity: 1, TypeArguments.Length: 1 } entry
            && entry.Name == "EntityEntry"
            && entry.ContainingNamespace?.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.ChangeTracking"
            && IsEntityTypeForMethod(entry.TypeArguments[0], entityType, method);
    }

    internal static bool IsShapePreservingQueryMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        var namespaceName = definition.ContainingNamespace?.ToDisplayString();
        var containingTypeName = definition.ContainingType?.Name;

        if (namespaceName == "System.Linq" && containingTypeName == "Queryable")
        {
            return definition.Name
                is "Where"
                    or "OrderBy"
                    or "OrderByDescending"
                    or "ThenBy"
                    or "ThenByDescending"
                    or "Skip"
                    or "Take"
                    or "Distinct"
                    or "Reverse"
                    or "AsQueryable";
        }

        if (namespaceName == "Microsoft.EntityFrameworkCore")
        {
            return definition.Name
                is "AsTracking"
                    or "AsNoTracking"
                    or "AsNoTrackingWithIdentityResolution"
                    or "Include"
                    or "ThenInclude"
                    or "IgnoreAutoIncludes"
                    or "IgnoreQueryFilters"
                    or "AsSplitQuery"
                    or "AsSingleQuery"
                    or "TagWith"
                    or "TagWithCallSite"
                    or "FromSql"
                    or "FromSqlRaw"
                    or "FromSqlInterpolated";
        }

        return false;
    }

    internal static bool IsSaveChanges(IInvocationOperation invocation)
    {
        for (
            IMethodSymbol? current = invocation.TargetMethod;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (IsDbContextSaveChangesDeclaration(current))
                return true;
        }

        return false;
    }

    private static bool IsDbContextSaveChangesDeclaration(IMethodSymbol method)
    {
        if (
            method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Arity != 0
            || method.ContainingType.Name != "DbContext"
            || method.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore"
        )
        {
            return false;
        }

        if (
            method.Name == "SaveChanges"
            && method.ReturnType.SpecialType == SpecialType.System_Int32
        )
        {
            return method.Parameters.Length == 0
                || method.Parameters.Length == 1
                    && IsValueParameter(method.Parameters[0], SpecialType.System_Boolean);
        }

        if (method.Name != "SaveChangesAsync" || !IsTaskOfInt32(method.ReturnType))
            return false;

        return method.Parameters.Length == 1 && IsCancellationTokenParameter(method.Parameters[0])
            || method.Parameters.Length == 2
                && IsValueParameter(method.Parameters[0], SpecialType.System_Boolean)
                && IsCancellationTokenParameter(method.Parameters[1]);
    }

    private static bool IsTaskOfInt32(ITypeSymbol type)
    {
        return type is INamedTypeSymbol taskType
            && taskType.Name == "Task"
            && taskType.Arity == 1
            && taskType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && taskType.TypeArguments[0].SpecialType == SpecialType.System_Int32;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol parameter)
    {
        return parameter.RefKind == RefKind.None
            && parameter.Type.Name == "CancellationToken"
            && parameter.Type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    private static bool IsValueParameter(IParameterSymbol parameter, SpecialType specialType)
    {
        return parameter.RefKind == RefKind.None && parameter.Type.SpecialType == specialType;
    }

    internal static bool IsTransactionTerminationMethod(IMethodSymbol method)
    {
        return method.Name
            is "Dispose"
                or "DisposeAsync"
                or "Commit"
                or "CommitAsync"
                or "Rollback"
                or "RollbackAsync";
    }

    internal static bool IsTransactionOperation(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod;
        if (
            target.Name
            is not (
                "BeginTransaction"
                or "BeginTransactionAsync"
                or "UseTransaction"
                or "UseTransactionAsync"
            )
        )
        {
            return false;
        }

        var definition = target.ReducedFrom ?? target;
        var isExtension =
            definition.IsStatic
            && definition.IsExtensionMethod
            && definition.ContainingType.OriginalDefinition
                is { Name: "RelationalDatabaseFacadeExtensions", Arity: 0 } extensionType
            && extensionType.ContainingNamespace?.ToDisplayString()
                == "Microsoft.EntityFrameworkCore";
        var isInstance =
            !definition.IsStatic
            && definition.ContainingType.OriginalDefinition
                is { Name: "DatabaseFacade", Arity: 0 } facadeType
            && facadeType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
        if (!isExtension && !isInstance)
            return false;

        var parameterOffset = isExtension ? 1 : 0;
        if (
            isExtension
            && (
                definition.Parameters.Length == 0
                || !IsEfDatabaseFacadeType(definition.Parameters[0].Type)
            )
        )
        {
            return false;
        }

        var parameters = definition.Parameters.Skip(parameterOffset).ToImmutableArray();
        if (definition.Name is "BeginTransaction" or "BeginTransactionAsync")
        {
            if (
                parameters.Length > 1
                || parameters.Length == 1 && !IsCancellationTokenParameter(parameters[0])
            )
            {
                return false;
            }

            return definition.Name == "BeginTransaction"
                ? parameters.Length == 0 && IsEfContextTransactionType(definition.ReturnType)
                : IsFrameworkGenericReturn(
                    definition.ReturnType,
                    "Task",
                    GetEfContextTransactionResult(definition.ReturnType)
                ) && IsEfContextTransactionResult(definition.ReturnType);
        }

        if (parameters.Length == 0 || !IsDbTransactionType(parameters[0].Type))
            return false;

        if (definition.Name == "UseTransaction")
        {
            return (
                    parameters.Length == 1
                    || parameters.Length == 2 && IsSystemGuidType(parameters[1].Type)
                ) && IsEfContextTransactionType(definition.ReturnType);
        }

        var hasValidAsyncParameters =
            parameters.Length == 2 && IsCancellationTokenParameter(parameters[1])
            || parameters.Length == 3
                && IsSystemGuidType(parameters[1].Type)
                && IsCancellationTokenParameter(parameters[2]);
        return definition.Name == "UseTransactionAsync"
            && hasValidAsyncParameters
            && IsEfContextTransactionResult(definition.ReturnType)
            && IsFrameworkGenericReturn(
                definition.ReturnType,
                "Task",
                GetEfContextTransactionResult(definition.ReturnType)
            );
    }

    private static bool IsSystemGuidType(ITypeSymbol type)
    {
        return type.OriginalDefinition.Name == "Guid"
            && type.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static bool IsEfDatabaseFacadeType(ITypeSymbol type)
    {
        return type.OriginalDefinition is INamedTypeSymbol { Name: "DatabaseFacade", Arity: 0 }
            && type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsEfContextTransactionType(ITypeSymbol type)
    {
        return type.OriginalDefinition
                is INamedTypeSymbol { Name: "IDbContextTransaction", Arity: 0 }
            && type.ContainingNamespace?.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Storage";
    }

    private static bool IsDbTransactionType(ITypeSymbol type)
    {
        return type.OriginalDefinition is INamedTypeSymbol { Name: "DbTransaction", Arity: 0 }
            && type.ContainingNamespace?.ToDisplayString() == "System.Data.Common";
    }

    private static bool IsEfContextTransactionResult(ITypeSymbol returnType)
    {
        return IsEfContextTransactionType(GetEfContextTransactionResult(returnType));
    }

    private static ITypeSymbol GetEfContextTransactionResult(ITypeSymbol returnType)
    {
        return returnType is INamedTypeSymbol { TypeArguments.Length: 1 } namedReturn
            ? namedReturn.TypeArguments[0]
            : returnType;
    }

    internal static bool IsUseTransactionOperation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name is "UseTransaction" or "UseTransactionAsync"
            && IsTransactionOperation(invocation);
    }

    internal static bool IsEfCoreMethod(IMethodSymbol method)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString();
        return namespaceName == "Microsoft.EntityFrameworkCore"
            || namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
                == true;
    }

    internal static bool IsEfDbContextEntryMethod(IMethodSymbol method)
    {
        for (IMethodSymbol? current = method; current != null; )
        {
            var definition = current.OriginalDefinition;
            if (IsEfDbContextEntryDeclaration(definition))
                return true;

            current = current.OverriddenMethod ?? definition.OverriddenMethod;
        }

        return false;
    }

    private static bool IsEfDbContextEntryDeclaration(IMethodSymbol method)
    {
        if (
            method.Name != "Entry"
            || method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Parameters.Length != 1
            || !IsEfType(method.ContainingType, "DbContext")
            || method.ContainingType.OriginalDefinition.Arity != 0
        )
        {
            return false;
        }

        if (method.Arity == 0)
        {
            return method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                && IsEfChangeTrackingType(method.ReturnType, "EntityEntry", 0);
        }

        return method.Arity == 1
            && method.TypeParameters.Length == 1
            && SymbolEqualityComparer.Default.Equals(
                method.Parameters[0].Type,
                method.TypeParameters[0]
            )
            && method.ReturnType is INamedTypeSymbol returnType
            && IsEfChangeTrackingType(returnType, "EntityEntry", 1)
            && SymbolEqualityComparer.Default.Equals(
                returnType.TypeArguments[0],
                method.TypeParameters[0]
            );
    }

    internal static bool IsEfEntityEntryStateProperty(IPropertySymbol property)
    {
        return property.Name == "State"
            && !property.IsStatic
            && IsEfChangeTrackingType(property.ContainingType, "EntityEntry")
            && IsEfType(property.Type, "EntityState");
    }

    internal static bool IsEfEntityEntryPropertyMethod(IMethodSymbol method)
    {
        if (
            method.Name != "Property"
            || method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Parameters.Length != 1
            || method.ContainingType.OriginalDefinition is not { } containingType
            || method.ReturnType.OriginalDefinition is not INamedTypeSymbol returnType
            || !IsEfChangeTrackingType(containingType, "EntityEntry")
            || !IsEfChangeTrackingType(returnType, "PropertyEntry")
        )
        {
            return false;
        }

        var parameterType = method.Parameters[0].Type;
        if (parameterType.SpecialType == SpecialType.System_String)
        {
            return method.Arity == 0 && containingType.Arity == 0 && returnType.Arity == 0
                || method.Arity == 1 && containingType.Arity == 1 && returnType.Arity == 2;
        }

        if (
            method.Arity != 1
            || containingType.Arity != 1
            || returnType.Arity != 2
            || parameterType.OriginalDefinition
                is not INamedTypeSymbol { Name: "Expression", Arity: 1 } expressionType
            || expressionType.ContainingNamespace?.ToDisplayString() != "System.Linq.Expressions"
            || parameterType is not INamedTypeSymbol namedParameterType
            || namedParameterType.TypeArguments.Length != 1
            || namedParameterType.TypeArguments[0]
                is not INamedTypeSymbol { Name: "Func", Arity: 2 } functionType
        )
        {
            return false;
        }

        return functionType.ContainingNamespace?.ToDisplayString() == "System";
    }

    internal static bool IsEfPropertyEntryIsModifiedProperty(IPropertySymbol property)
    {
        return property.Name == "IsModified"
            && !property.IsStatic
            && property.Type.SpecialType == SpecialType.System_Boolean
            && IsEfChangeTrackingType(property.ContainingType, "PropertyEntry");
    }

    internal static bool IsEfEntityEntryReloadMethod(IMethodSymbol method)
    {
        if (
            method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.Arity != 0
            || !IsEfChangeTrackingType(method.ContainingType, "EntityEntry")
        )
        {
            return false;
        }

        if (method.Name == "Reload")
            return method.Parameters.Length == 0 && method.ReturnsVoid;

        return method.Name == "ReloadAsync"
            && method.Parameters.Length == 1
            && method.Parameters[0].Type.Name == "CancellationToken"
            && method.Parameters[0].Type.ContainingNamespace?.ToDisplayString()
                == "System.Threading"
            && method.ReturnType.OriginalDefinition
                is INamedTypeSymbol { Name: "Task", Arity: 0 } taskType
            && taskType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    internal static bool IsEfChangeTrackerClearMethod(IMethodSymbol method)
    {
        return method.Name == "Clear"
            && method.MethodKind == MethodKind.Ordinary
            && !method.IsStatic
            && method.Arity == 0
            && method.Parameters.Length == 0
            && method.ReturnsVoid
            && IsEfChangeTrackingType(method.ContainingType, "ChangeTracker", 0);
    }

    internal static bool IsEfChangeTrackerAcceptAllChangesMethod(IMethodSymbol method)
    {
        return method.Name == "AcceptAllChanges"
            && method.MethodKind == MethodKind.Ordinary
            && !method.IsStatic
            && method.Arity == 0
            && method.Parameters.Length == 0
            && method.ReturnsVoid
            && IsEfChangeTrackingType(method.ContainingType, "ChangeTracker", 0);
    }

    internal static bool IsEfChangeTrackerDetectChangesMethod(IMethodSymbol method)
    {
        return method.Name == "DetectChanges"
            && method.MethodKind == MethodKind.Ordinary
            && !method.IsStatic
            && method.Arity == 0
            && method.Parameters.Length == 0
            && method.ReturnsVoid
            && IsEfChangeTrackingType(method.ContainingType, "ChangeTracker", 0);
    }

    internal static bool IsEfDbContextChangeTrackerProperty(IPropertySymbol property)
    {
        return IsPropertyDeclaredOnEfType(property, "ChangeTracker", "DbContext")
            && IsEfChangeTrackingType(property.Type, "ChangeTracker", 0);
    }

    internal static bool IsEfChangeTrackerProperty(IPropertySymbol property, string propertyName)
    {
        if (
            property.Name != propertyName
            || !IsEfChangeTrackingType(property.ContainingType, "ChangeTracker", 0)
        )
        {
            return false;
        }

        return propertyName switch
        {
            "QueryTrackingBehavior" => IsEfType(property.Type, "QueryTrackingBehavior"),
            "AutoDetectChangesEnabled" => property.Type.SpecialType == SpecialType.System_Boolean,
            _ => false,
        };
    }

    private static bool IsPropertyDeclaredOnEfType(
        IPropertySymbol property,
        string propertyName,
        string containingTypeName
    )
    {
        return property.Name == propertyName
            && property.ContainingType.OriginalDefinition.Name == containingTypeName
            && property.ContainingType.OriginalDefinition.Arity == 0
            && property.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsEfType(ITypeSymbol type, string typeName)
    {
        return type.OriginalDefinition.Name == typeName
            && type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsEfChangeTrackingType(ITypeSymbol type, string typeName, int? arity = null)
    {
        return type.OriginalDefinition is INamedTypeSymbol definition
            && definition.Name == typeName
            && (!arity.HasValue || definition.Arity == arity.Value)
            && definition.ContainingNamespace?.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.ChangeTracking";
    }

    internal static bool IsDbContextType(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (
                current.Name == "DbContext"
                && current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore"
            )
                return true;
        }

        return false;
    }

    internal static bool TryGetDbSetEntityType(ITypeSymbol? type, out INamedTypeSymbol entityType)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (
                current.Name == "DbSet"
                && current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore"
                && current.TypeArguments.Length == 1
                && current.TypeArguments[0] is INamedTypeSymbol namedEntity
            )
            {
                entityType = namedEntity;
                return true;
            }
        }

        entityType = null!;
        return false;
    }

    internal static bool IsScalarProperty(IPropertySymbol property)
    {
        if (
            property.IsIndexer
            || property
                .GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString()
                        == "System.ComponentModel.DataAnnotations.Schema.DatabaseGeneratedAttribute"
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is 2
                )
        )
        {
            return false;
        }

        var type = property.Type;
        if (
            type.SpecialType != SpecialType.None
            || type.TypeKind == TypeKind.Enum
            || type.IsValueType
        )
            return true;

        return type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Byte;
    }

    internal static bool TryGetRootParameter(IOperation? operation, out IParameterSymbol parameter)
    {
        if (operation != null && Unwrap(operation) is IParameterReferenceOperation reference)
        {
            parameter = reference.Parameter;
            return true;
        }

        parameter = null!;
        return false;
    }

    internal static bool TryGetRootSymbol(IOperation? operation, out ISymbol symbol)
    {
        if (operation != null)
        {
            switch (Unwrap(operation))
            {
                case IParameterReferenceOperation parameter:
                    symbol = parameter.Parameter;
                    return true;
                case ILocalReferenceOperation local:
                    symbol = local.Local;
                    return true;
            }
        }

        symbol = null!;
        return false;
    }

    internal static bool ContainsPropertyRead(
        IOperation operation,
        IPropertySymbol property,
        ISymbol rootSymbol
    )
    {
        var collector = new OperationCollector();
        collector.Visit(operation);
        return collector.PropertyReferences.Any(reference =>
            !IsInsideNameOf(reference)
            && SymbolEqualityComparer.Default.Equals(reference.Property, property)
            && TryGetRootSymbol(reference.Instance, out var root)
            && SymbolEqualityComparer.Default.Equals(root, rootSymbol)
        );
    }

    internal static bool IsInsideNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is INameOfOperation)
                return true;
        }

        return false;
    }

    internal static bool IsGuardedByPropertyRead(
        IOperation mutation,
        IPropertySymbol property,
        ISymbol rootSymbol
    )
    {
        for (var current = mutation.Parent; current != null; current = current.Parent)
        {
            if (
                current is IConditionalOperation conditional
                && ContainsPropertyRead(conditional.Condition, property, rootSymbol)
            )
            {
                return true;
            }

            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                break;
        }

        return false;
    }
}
