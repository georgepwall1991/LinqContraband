using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private delegate bool EntityOriginResolver(IOperation? operation, out EntityOrigin origin);

    private static readonly EntityOrigin FallbackEntityOrigin = new(-1, null, true);

    private static bool TryCollectOriginAwareNavigationAccesses(
        IOperation executableRoot,
        IInvocationOperation materializer,
        ILocalSymbol resultLocal,
        bool returnsCollection,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken,
        out List<NavigationAccess> accesses
    )
    {
        accesses = null!;

        if (
            !TryGetFlowGraph(
                executableRoot,
                materializer.SemanticModel,
                flowGraphCache,
                cancellationToken,
                out var graph
            )
        )
            return false;

        var context = new OriginFlowContext(
            executableRoot,
            materializer,
            resultLocal,
            returnsCollection,
            entityType,
            entityTypes,
            cancellationToken
        );

        context.Build();
        if (!context.TryMapEventsToBlocks(graph))
            return false;

        accesses = new List<NavigationAccess>();
        foreach (var candidate in context.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                TryGetProvenMissingAccess(
                    graph,
                    context,
                    candidate,
                    cancellationToken,
                    out var provenAccess
                )
            )
            {
                accesses.Add(provenAccess);
            }
        }

        return true;
    }

    private static bool IsMaterializedCollectionActiveAtInvocation(
        IOperation executableRoot,
        IInvocationOperation materializer,
        ILocalSymbol resultLocal,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        IInvocationOperation invocation,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken
    )
    {
        if (
            !TryGetFlowGraph(
                executableRoot,
                materializer.SemanticModel,
                flowGraphCache,
                cancellationToken,
                out var graph
            )
        )
        {
            return false;
        }

        var context = new OriginFlowContext(
            executableRoot,
            materializer,
            resultLocal,
            returnsCollection: true,
            entityType,
            entityTypes,
            cancellationToken
        );
        context.Build();
        var probe = context.AddRootProbe(invocation.Syntax);
        if (!context.TryMapEventsToBlocks(graph))
            return false;

        return IsProvenActiveAtProbe(graph, context, probe, cancellationToken);
    }

    private static bool IsProvenActiveAtProbe(
        ControlFlowGraph graph,
        OriginFlowContext context,
        FlowAccessCandidate probe,
        CancellationToken cancellationToken
    )
    {
        var statesByBlock = new Dictionary<int, HashSet<FlowProbeState>>();
        var worklist = new Queue<FlowWorkItem>();
        Enqueue(graph.Blocks[0], default, statesByBlock, worklist);

        var sawActive = false;
        var sawUncertain = false;
        while (worklist.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = worklist.Dequeue();
            var state = item.State;
            var reachedProbe = false;

            if (context.EventsByBlock.TryGetValue(item.Block.Ordinal, out var events))
            {
                foreach (var flowEvent in events)
                {
                    if (
                        flowEvent.Kind == FlowEventKind.Access
                        && flowEvent.AccessId == probe.AccessId
                    )
                    {
                        reachedProbe = true;
                        if (state.IsActive && !state.RootUnknown)
                            sawActive = true;
                        else
                            sawUncertain = true;
                        break;
                    }

                    ApplyEvent(flowEvent, probe, ref state);
                }
            }

            if (reachedProbe)
                continue;

            foreach (var successor in GetSuccessors(item.Block))
                Enqueue(successor, state, statesByBlock, worklist);
        }

        return sawActive && !sawUncertain;
    }

    private static bool TryCollectOriginAwareNavigationAccesses(
        IOperation parentExecutableRoot,
        IAnonymousFunctionOperation callback,
        IParameterSymbol callbackParameter,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken,
        out List<NavigationAccess> accesses
    )
    {
        accesses = null!;
        if (
            !TryGetFlowGraph(
                parentExecutableRoot,
                callback.SemanticModel,
                flowGraphCache,
                cancellationToken,
                out var parentGraph
            ) || !TryGetCallbackFlowGraph(parentGraph, callback, cancellationToken, out var graph)
        )
        {
            return false;
        }

        var context = new OriginFlowContext(
            callback,
            callbackParameter,
            entityType,
            entityTypes,
            cancellationToken
        );
        context.Build();
        if (!context.TryMapEventsToBlocks(graph))
            return false;

        accesses = new List<NavigationAccess>();
        foreach (var candidate in context.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                TryGetProvenMissingAccess(
                    graph,
                    context,
                    candidate,
                    cancellationToken,
                    out var access
                )
            )
                accesses.Add(access);
        }

        return true;
    }

    private static bool TryGetCallbackFlowGraph(
        ControlFlowGraph parentGraph,
        IAnonymousFunctionOperation callback,
        CancellationToken cancellationToken,
        out ControlFlowGraph graph
    )
    {
        foreach (var block in parentGraph.Blocks)
        {
            if (
                TryFindFlowAnonymousFunction(block.Operations, callback, out var anonymous)
                || (
                    block.BranchValue != null
                    && TryFindFlowAnonymousFunction(
                        System.Collections.Immutable.ImmutableArray.Create(block.BranchValue),
                        callback,
                        out anonymous
                    )
                )
            )
            {
                try
                {
                    graph = parentGraph.GetAnonymousFunctionControlFlowGraph(
                        anonymous,
                        cancellationToken
                    );
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }
            }
        }

        graph = null!;
        return false;
    }

    private static bool TryFindFlowAnonymousFunction(
        System.Collections.Immutable.ImmutableArray<IOperation> operations,
        IAnonymousFunctionOperation callback,
        out IFlowAnonymousFunctionOperation anonymous
    )
    {
        foreach (var operation in operations)
        {
            if (TryFind(operation, callback, out anonymous))
            {
                return true;
            }
        }

        anonymous = null!;
        return false;

        static bool TryFind(
            IOperation operation,
            IAnonymousFunctionOperation callback,
            out IFlowAnonymousFunctionOperation anonymous
        )
        {
            if (
                operation is IFlowAnonymousFunctionOperation flowAnonymous
                && flowAnonymous.Syntax.SyntaxTree == callback.Syntax.SyntaxTree
                && flowAnonymous.Syntax.Span == callback.Syntax.Span
            )
            {
                anonymous = flowAnonymous;
                return true;
            }

            foreach (var child in operation.ChildOperations)
            {
                if (TryFind(child, callback, out anonymous))
                    return true;
            }

            anonymous = null!;
            return false;
        }
    }

    private static bool TryCollectOriginAwareNavigationAccesses(
        IOperation executableRoot,
        IForEachLoopOperation rootForEach,
        INamedTypeSymbol entityType,
        HashSet<INamedTypeSymbol> entityTypes,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken,
        out List<NavigationAccess> accesses
    )
    {
        accesses = null!;

        if (
            !TryGetFlowGraph(
                executableRoot,
                rootForEach.SemanticModel,
                flowGraphCache,
                cancellationToken,
                out var graph
            )
        )
            return false;

        var context = new OriginFlowContext(
            executableRoot,
            rootForEach,
            entityType,
            entityTypes,
            cancellationToken
        );

        context.Build();
        if (!context.TryMapEventsToBlocks(graph))
            return false;

        accesses = new List<NavigationAccess>();
        foreach (var candidate in context.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                TryGetProvenMissingAccess(
                    graph,
                    context,
                    candidate,
                    cancellationToken,
                    out var provenAccess
                )
            )
            {
                accesses.Add(provenAccess);
            }
        }

        return true;
    }

    private static bool TryGetFlowGraph(
        IOperation executableRoot,
        SemanticModel? semanticModel,
        ConditionalWeakTable<IOperation, FlowGraphHolder> flowGraphCache,
        CancellationToken cancellationToken,
        out ControlFlowGraph graph
    )
    {
        if (flowGraphCache.TryGetValue(executableRoot, out var cached))
        {
            graph = cached.Graph!;
            return graph != null;
        }

        ControlFlowGraph? created;
        try
        {
            created = executableRoot switch
            {
                IMethodBodyOperation methodBody when methodBody.Parent == null =>
                    ControlFlowGraph.Create(methodBody, cancellationToken),
                IConstructorBodyOperation constructorBody when constructorBody.Parent == null =>
                    ControlFlowGraph.Create(constructorBody, cancellationToken),
                _ when semanticModel != null => ControlFlowGraph.Create(
                    executableRoot.Syntax,
                    semanticModel,
                    cancellationToken
                ),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            created = null;
        }
        catch (InvalidOperationException)
        {
            created = null;
        }

        var holder = new FlowGraphHolder(created);
        try
        {
            flowGraphCache.Add(executableRoot, holder);
        }
        catch (ArgumentException)
        {
            if (flowGraphCache.TryGetValue(executableRoot, out var raced))
                holder = raced;
        }

        graph = holder.Graph!;
        return graph != null;
    }

    private static bool TryGetProvenMissingAccess(
        ControlFlowGraph graph,
        OriginFlowContext context,
        FlowAccessCandidate candidate,
        CancellationToken cancellationToken,
        out NavigationAccess provenAccess
    )
    {
        provenAccess = candidate.Access;
        var statesByBlock = new Dictionary<int, HashSet<FlowProbeState>>();
        var worklist = new Queue<FlowWorkItem>();

        Enqueue(graph.Blocks[0], default, statesByBlock, worklist);

        var sawKnownUnsatisfied = false;
        var sawUncertain = false;
        var knownBindings = new HashSet<string>(StringComparer.Ordinal);
        var knownPaths = new HashSet<string>(StringComparer.Ordinal);

        while (worklist.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = worklist.Dequeue();
            var state = item.State;
            var reachedCandidate = false;

            if (context.EventsByBlock.TryGetValue(item.Block.Ordinal, out var events))
            {
                foreach (var flowEvent in events)
                {
                    if (flowEvent.Kind == FlowEventKind.Access)
                    {
                        if (flowEvent.AccessId != candidate.AccessId)
                            continue;

                        reachedCandidate = true;
                        if (
                            !state.IsActive
                            || !state.OriginBound
                            || state.OriginUnknown
                            || (state.RootUnknown && !state.OriginIndependentOfRoot)
                        )
                        {
                            sawUncertain = true;
                        }
                        else
                        {
                            var effectivePath = GetEffectiveAccessPath(candidate, state);
                            var bindingPrefix = state.GetOriginPrefix(candidate.Origin);
                            if (effectivePath == null || bindingPrefix == null)
                            {
                                sawUncertain = true;
                                break;
                            }

                            knownBindings.Add(
                                state.GetGeneration(candidate.Origin) + ":" + bindingPrefix
                            );
                            knownPaths.Add(effectivePath);
                            if (!state.PathSatisfied)
                                sawKnownUnsatisfied = true;
                        }

                        break;
                    }

                    ApplyEvent(flowEvent, candidate, ref state);
                }
            }

            if (reachedCandidate)
                continue;

            foreach (var successor in GetSuccessors(item.Block))
                Enqueue(successor, state, statesByBlock, worklist);
        }

        if (
            !sawKnownUnsatisfied
            || sawUncertain
            || knownBindings.Count != 1
            || knownPaths.Count != 1
        )
        {
            return false;
        }

        foreach (var path in knownPaths)
        {
            provenAccess = new NavigationAccess(path, candidate.Access.Syntax);
            break;
        }

        return true;
    }

    private static void ApplyEvent(
        FlowEvent flowEvent,
        FlowAccessCandidate candidate,
        ref FlowProbeState state
    )
    {
        switch (flowEvent.Kind)
        {
            case FlowEventKind.Materialize:
                var materializedOrigin = GetUltimateSourceOrigin(candidate.Origin);
                var preMaterializationGenerations = state.OriginGenerations;
                var capturedOriginIds = state.CapturedOriginIds;
                var bindingSnapshots = state.BindingSnapshots;
                var unknownGenerations = state.UnknownGenerations;
                var satisfiedGenerations = state.SatisfiedGenerations;
                var originPrefixes = state.OriginPrefixes;
                var prefixSnapshots = state.PrefixSnapshots;
                state = new FlowProbeState(
                    isActive: true,
                    rootUnknown: false,
                    originBound: false,
                    originUnknown: state.IsOriginCaptured(candidate.Origin),
                    pathSatisfied: false,
                    originIndependentOfRoot: false,
                    iterationSourceCaptured: false,
                    aliasSourceLinked: false,
                    originGenerations: preMaterializationGenerations,
                    capturedOriginIds: capturedOriginIds,
                    bindingSnapshots: bindingSnapshots,
                    unknownGenerations: unknownGenerations,
                    satisfiedGenerations: satisfiedGenerations,
                    originPrefixes: originPrefixes,
                    prefixSnapshots: prefixSnapshots
                );
                if (materializedOrigin.InitiallyBound)
                {
                    state = state.WithGeneration(
                        materializedOrigin,
                        CreateBindingGeneration(
                            materializedOrigin,
                            materializedOrigin.BindingPosition
                        )
                    );
                }

                state = state.WithOrigin(
                    originBound: state.GetGeneration(candidate.Origin) != -1,
                    originUnknown: state.IsOriginCaptured(candidate.Origin),
                    pathSatisfied: false,
                    originIndependentOfRoot: false,
                    iterationSourceCaptured: false,
                    aliasSourceLinked: false
                );
                break;

            case FlowEventKind.BindOrigin when flowEvent.Origin != null:
                state = state.WithGeneration(
                    flowEvent.Origin,
                    state.IsActive && !state.RootUnknown
                        ? CreateBindingGeneration(flowEvent.Origin, flowEvent.Position)
                        : -1
                );
                state = state.WithOriginPrefix(flowEvent.Origin, flowEvent.Origin.NavigationPrefix);

                if (IsAliasSourceEventAfterBinding(flowEvent, candidate))
                {
                    if (state.AliasSourceLinked)
                        state = state.WithAliasSourceLinked(false);
                    break;
                }

                if (!OriginEventAffectsCandidate(flowEvent, candidate, state))
                    break;

                if (state.IsActive && !state.RootUnknown)
                {
                    state = state.WithOrigin(
                        originBound: true,
                        originUnknown: false,
                        pathSatisfied: false,
                        originIndependentOfRoot: true,
                        iterationSourceCaptured: state.IterationSourceCaptured,
                        aliasSourceLinked: state.AliasSourceLinked
                    );
                }
                else if (state.IsActive)
                {
                    state = state.WithOrigin(
                        originBound: false,
                        originUnknown: true,
                        pathSatisfied: false,
                        originIndependentOfRoot: false,
                        iterationSourceCaptured: state.IterationSourceCaptured,
                        aliasSourceLinked: state.AliasSourceLinked
                    );
                }

                break;

            case FlowEventKind.BindAliasOrigin when flowEvent.Origin != null:
                var oldAliasGeneration = state.GetGeneration(flowEvent.Origin);
                var oldAliasPrefix = state.GetOriginPrefix(flowEvent.Origin);
                var isFreshIterationStorage = flowEvent.IsFreshIterationStorage;
                if (isFreshIterationStorage)
                {
                    state = state
                        .WithoutReboundOriginFacts(flowEvent.Origin)
                        .WithoutCapturedOrigin(flowEvent.Origin);
                }
                var aliasGeneration = -1L;
                string? aliasPrefix = null;
                if (flowEvent.SnapshotId >= 0)
                {
                    aliasGeneration = state.GetBindingSnapshot(flowEvent.SnapshotId);
                    aliasPrefix = state.GetSnapshotPrefix(flowEvent.SnapshotId);
                }
                else if (flowEvent.RelatedOrigin != null)
                {
                    aliasGeneration = GetOrCreateKnownGeneration(
                        flowEvent.RelatedOrigin,
                        ref state
                    );
                    aliasPrefix = state.GetOriginPrefix(flowEvent.RelatedOrigin);
                    if (state.RootUnknown && flowEvent.RelatedOrigin.Local == null)
                    {
                        aliasGeneration = -1;
                        aliasPrefix = null;
                    }
                }

                var keepsSameBinding =
                    aliasGeneration != -1
                    && oldAliasGeneration == aliasGeneration
                    && string.Equals(oldAliasPrefix, aliasPrefix, StringComparison.Ordinal);
                state = state.WithGeneration(flowEvent.Origin, aliasGeneration);
                state = state.WithOriginPrefix(flowEvent.Origin, aliasPrefix);

                var bindingAffectsCandidate =
                    ReferenceEquals(flowEvent.Origin, candidate.Origin)
                    || IsLiveAncestorBinding(flowEvent.Origin, candidate);
                if (state.IsActive && bindingAffectsCandidate)
                {
                    var capturedBinding =
                        state.IsOriginCaptured(flowEvent.Origin)
                        || (
                            flowEvent.RelatedOrigin != null
                            && state.IsOriginCaptured(flowEvent.RelatedOrigin)
                        );
                    var boundAccessPath =
                        GetEffectiveAccessPath(candidate, state) ?? candidate.Access.Path;
                    var sourceUnknown =
                        aliasGeneration != -1
                        && state.IsGenerationUnknown(aliasGeneration, boundAccessPath);
                    var sourceSatisfied =
                        aliasGeneration != -1
                        && state.IsGenerationSatisfied(aliasGeneration, boundAccessPath);
                    state = state.WithOrigin(
                        originBound: aliasGeneration != -1,
                        originUnknown: aliasGeneration == -1
                            || capturedBinding
                            || sourceUnknown
                            || (
                                keepsSameBinding && !isFreshIterationStorage && state.OriginUnknown
                            ),
                        pathSatisfied: sourceSatisfied
                            || (
                                keepsSameBinding && !isFreshIterationStorage && state.PathSatisfied
                            ),
                        originIndependentOfRoot: aliasGeneration != -1,
                        iterationSourceCaptured: state.IterationSourceCaptured,
                        aliasSourceLinked: true
                    );
                }
                else if (
                    state.IsActive
                    && state.IsOriginCaptured(flowEvent.Origin)
                    && OriginEventAffectsCandidate(flowEvent, candidate, state)
                )
                {
                    state = state.WithOriginUnknown();
                }

                break;

            case FlowEventKind.SnapshotOrigin
                when flowEvent.Origin != null && flowEvent.SnapshotId >= 0:
                var snapshotGeneration = GetOrCreateKnownGeneration(flowEvent.Origin, ref state);
                if (state.RootUnknown && flowEvent.Origin.Local == null)
                    snapshotGeneration = -1;
                state = state.WithBindingSnapshot(
                    flowEvent.SnapshotId,
                    snapshotGeneration,
                    snapshotGeneration == -1 ? null : state.GetOriginPrefix(flowEvent.Origin)
                );
                break;

            case FlowEventKind.BindIterationOrigin when flowEvent.Origin != null:
                state = state
                    .WithoutReboundOriginFacts(flowEvent.Origin)
                    .WithoutCapturedOrigin(flowEvent.Origin);
                var parentOrigin = flowEvent.Origin.AliasSourceOrigin;
                var parentIsUncertain =
                    parentOrigin != null
                    && HasUncertainOriginOrAncestor(
                        parentOrigin,
                        GetEffectiveAccessPath(candidate, state) ?? candidate.Access.Path,
                        state
                    );
                var canBindIteration =
                    state.IsActive && (!state.RootUnknown || state.IterationSourceCaptured);
                state = state.WithGeneration(
                    flowEvent.Origin,
                    canBindIteration
                        ? CreateBindingGeneration(flowEvent.Origin, flowEvent.Position)
                        : -1
                );
                state = state.WithOriginPrefix(
                    flowEvent.Origin,
                    canBindIteration ? flowEvent.Origin.NavigationPrefix : null
                );

                if (IsAliasSourceEventAfterBinding(flowEvent, candidate))
                {
                    if (state.AliasSourceLinked)
                        state = state.WithAliasSourceLinked(false);
                    break;
                }

                if (!OriginEventAffectsCandidate(flowEvent, candidate, state))
                    break;

                if (state.IsActive && (!state.RootUnknown || state.IterationSourceCaptured))
                {
                    state = state.WithOrigin(
                        originBound: true,
                        originUnknown: parentIsUncertain,
                        pathSatisfied: false,
                        originIndependentOfRoot: !parentIsUncertain,
                        iterationSourceCaptured: true,
                        aliasSourceLinked: state.AliasSourceLinked
                    );
                }
                else if (state.IsActive)
                {
                    state = state.WithOrigin(
                        originBound: false,
                        originUnknown: true,
                        pathSatisfied: false,
                        originIndependentOfRoot: false,
                        iterationSourceCaptured: false,
                        aliasSourceLinked: state.AliasSourceLinked
                    );
                }

                break;

            case FlowEventKind.UnbindOrigin when flowEvent.Origin != null:
                var unbindAffectsCandidate =
                    ReferenceEquals(flowEvent.Origin, candidate.Origin)
                    || IsLiveAncestorBinding(flowEvent.Origin, candidate);
                state = state.WithGeneration(flowEvent.Origin, -1);
                state = state.WithOriginPrefix(flowEvent.Origin, null);

                if (IsAliasSourceEventAfterBinding(flowEvent, candidate))
                {
                    if (state.AliasSourceLinked)
                        state = state.WithAliasSourceLinked(false);
                    break;
                }

                if (
                    !unbindAffectsCandidate
                    && !OriginEventAffectsCandidate(flowEvent, candidate, state)
                )
                    break;

                if (state.IsActive)
                {
                    state = state.WithOrigin(
                        originBound: false,
                        originUnknown: true,
                        pathSatisfied: false,
                        originIndependentOfRoot: false,
                        iterationSourceCaptured: state.IterationSourceCaptured,
                        aliasSourceLinked: state.AliasSourceLinked
                    );
                }

                break;

            case FlowEventKind.ReassignRoot:
                if (state.IsActive)
                {
                    if (flowEvent.RelatedOrigin != null)
                        state = state.WithGeneration(flowEvent.RelatedOrigin, -1);

                    var originWasDetached =
                        candidate.CanDetachFromRoot
                        && state.OriginBound
                        && !state.OriginUnknown
                        && candidate.BindingPosition >= 0
                        && candidate.BindingPosition < flowEvent.Position;
                    state = state.WithRootReassignment(originWasDetached);
                    if (candidate.IsAliasBinding && candidate.BindingPosition < flowEvent.Position)
                        state = state.WithAliasSourceLinked(false);
                }

                break;

            case FlowEventKind.EscapeRoot:
                if (state.IsActive && !(state.RootUnknown && state.OriginIndependentOfRoot))
                    state = state.WithRootEscape();
                break;

            case FlowEventKind.EscapeOrigin when flowEvent.Origin != null:
                var escapedGeneration = GetOrCreateKnownGeneration(flowEvent.Origin, ref state);
                if (
                    state.IsActive
                    && escapedGeneration != -1
                    && (flowEvent.Path ?? state.GetOriginPrefix(flowEvent.Origin))
                        is string escapedPrefix
                )
                {
                    state = state.WithUnknownGeneration(escapedGeneration, escapedPrefix);
                }

                if (
                    state.IsActive
                    && state.OriginBound
                    && (
                        OriginEventAffectsCandidate(flowEvent, candidate, state)
                        || IsPostBindingRelatedOriginEventAffectingCandidate(
                            flowEvent,
                            candidate,
                            state
                        )
                    )
                )
                {
                    state = state.WithOriginUnknown();
                }
                break;

            case FlowEventKind.CaptureOrigin when flowEvent.Origin != null:
                state = state.WithCapturedOrigin(flowEvent.Origin);
                var capturedGeneration = GetOrCreateKnownGeneration(flowEvent.Origin, ref state);
                if (
                    state.IsActive
                    && capturedGeneration != -1
                    && state.GetOriginPrefix(flowEvent.Origin) is string capturedPrefix
                )
                {
                    state = state.WithUnknownGeneration(capturedGeneration, capturedPrefix);
                }
                if (
                    state.IsActive
                    && state.OriginBound
                    && (
                        OriginEventAffectsCandidate(flowEvent, candidate, state)
                        || IsPostBindingRelatedOriginEventAffectingCandidate(
                            flowEvent,
                            candidate,
                            state
                        )
                    )
                )
                {
                    state = state.WithOriginUnknown();
                }

                break;

            case FlowEventKind.InvalidateCollection
                when flowEvent.Origin != null && flowEvent.Path != null:
                var invalidatedGeneration = GetOrCreateKnownGeneration(flowEvent.Origin, ref state);
                if (state.IsActive && invalidatedGeneration != -1)
                {
                    state = state.WithUnknownGeneration(invalidatedGeneration, flowEvent.Path);
                }

                break;

            case FlowEventKind.SatisfyPath when flowEvent.Origin != null && flowEvent.Path != null:
                if (state.IsActive && !state.RootUnknown)
                {
                    var satisfiedGeneration = GetOrCreateKnownGeneration(
                        flowEvent.Origin,
                        ref state
                    );
                    if (satisfiedGeneration != -1)
                    {
                        state = state.WithSatisfiedGeneration(satisfiedGeneration, flowEvent.Path);
                    }
                }

                if (
                    state.IsActive
                    && (!state.RootUnknown || state.OriginIndependentOfRoot)
                    && state.OriginBound
                    && !state.OriginUnknown
                    && GetEffectiveAccessPath(candidate, state) is string effectivePath
                    && PathCovers(flowEvent.Path, effectivePath)
                    && !ReplacesDetachedNavigationPrefix(flowEvent, candidate, state)
                    && OriginEventAffectsCandidate(flowEvent, candidate, state)
                )
                {
                    state = state.WithPathSatisfied();
                }
                break;
        }
    }

    private static bool ReplacesDetachedNavigationPrefix(
        FlowEvent flowEvent,
        FlowAccessCandidate candidate,
        FlowProbeState state
    )
    {
        if (
            flowEvent.Origin == null
            || flowEvent.Path == null
            || candidate.AccessLocal == null
            || candidate.Origin.Local == null
            || state.GetOriginPrefix(candidate.Origin) is not string candidatePrefix
            || candidatePrefix.Length == 0
            || !PathCovers(flowEvent.Path, candidatePrefix)
        )
        {
            return false;
        }

        return flowEvent.Origin.Local == null
            || !SymbolEqualityComparer.Default.Equals(
                flowEvent.Origin.Local,
                candidate.AccessLocal
            );
    }

    private static bool IsAliasSourceEventAfterBinding(
        FlowEvent flowEvent,
        FlowAccessCandidate candidate
    )
    {
        return candidate.Origin.AliasSourceOrigin != null
            && flowEvent.Origin != null
            && IsAncestorOrigin(flowEvent.Origin, candidate.Origin)
            && !IsLiveAncestorBinding(flowEvent.Origin, candidate)
            && flowEvent.Position > candidate.BindingPosition;
    }

    private static bool IsLiveAncestorBinding(
        EntityOrigin eventOrigin,
        FlowAccessCandidate candidate
    )
    {
        if (!IsAncestorOrigin(eventOrigin, candidate.Origin))
            return false;

        if (eventOrigin.Local == null)
            return candidate.AccessLocal == null;

        return candidate.AccessLocal != null
            && SymbolEqualityComparer.Default.Equals(eventOrigin.Local, candidate.AccessLocal);
    }

    private static bool HasUncertainOriginOrAncestor(
        EntityOrigin origin,
        string accessPath,
        FlowProbeState state
    )
    {
        for (var current = origin; current != null; current = current.AliasSourceOrigin)
        {
            var generation = state.GetGeneration(current);
            if (
                generation == -1
                || state.IsOriginCaptured(current)
                || state.IsGenerationUnknown(generation, accessPath)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostBindingRelatedOriginEventAffectingCandidate(
        FlowEvent flowEvent,
        FlowAccessCandidate candidate,
        FlowProbeState state
    )
    {
        if (
            flowEvent.Origin == null
            || !candidate.Origin.IsIteration
            || flowEvent.Position <= candidate.BindingPosition
            || GetEffectiveAccessPath(candidate, state) is not string effectivePath
        )
        {
            return false;
        }

        return (
                IsAncestorOrigin(flowEvent.Origin, candidate.Origin)
                || SharesLiveUltimateSource(flowEvent.Origin, candidate.Origin, state)
            ) && EventScopesAccess(flowEvent, state, effectivePath);
    }

    private static bool OriginEventAffectsCandidate(
        FlowEvent flowEvent,
        FlowAccessCandidate candidate,
        FlowProbeState state
    )
    {
        var eventOrigin = flowEvent.Origin;
        if (eventOrigin == null)
            return false;

        var effectivePath = GetEffectiveAccessPath(candidate, state);
        if (effectivePath == null)
            return false;

        if (
            ReferenceEquals(eventOrigin, candidate.Origin)
            && EventScopesAccess(flowEvent, state, effectivePath)
        )
            return true;

        var eventGeneration = state.GetGeneration(eventOrigin);
        var candidateGeneration = state.GetGeneration(candidate.Origin);
        if (
            eventGeneration != -1
            && eventGeneration == candidateGeneration
            && EventScopesAccess(flowEvent, state, effectivePath)
        )
            return true;

        if (
            IsAncestorOrigin(eventOrigin, candidate.Origin)
            && EventScopesAccess(flowEvent, state, effectivePath)
        )
        {
            return candidateGeneration == -1 && flowEvent.Position < candidate.BindingPosition;
        }

        return false;
    }

    private static bool EventScopesAccess(
        FlowEvent flowEvent,
        FlowProbeState state,
        string accessPath
    )
    {
        return flowEvent.Path != null
            ? PathCovers(flowEvent.Path, accessPath)
            : flowEvent.Origin != null
                && OriginPrefixScopesAccess(flowEvent.Origin, state, accessPath);
    }

    private static bool OriginPrefixScopesAccess(
        EntityOrigin origin,
        FlowProbeState state,
        string accessPath
    )
    {
        var prefix = state.GetOriginPrefix(origin);
        return prefix != null && (prefix.Length == 0 || PathCovers(prefix, accessPath));
    }

    private static string? GetEffectiveAccessPath(
        FlowAccessCandidate candidate,
        FlowProbeState state
    )
    {
        var currentPrefix = state.GetOriginPrefix(candidate.Origin);
        if (currentPrefix == null)
            return null;

        var declaredPrefix = candidate.Origin.NavigationPrefix;
        if (declaredPrefix.Length == 0)
        {
            return currentPrefix.Length == 0
                ? candidate.Access.Path
                : CombineNavigationPath(currentPrefix, candidate.Access.Path);
        }

        if (!PathCovers(declaredPrefix, candidate.Access.Path))
            return null;

        var suffix = candidate.Access.Path.Substring(declaredPrefix.Length);
        if (currentPrefix.Length == 0)
            return suffix.Length > 0 && suffix[0] == '.' ? suffix.Substring(1) : suffix;

        return currentPrefix + suffix;
    }

    private static long GetOrCreateKnownGeneration(EntityOrigin origin, ref FlowProbeState state)
    {
        var generation = state.GetGeneration(origin);
        if (generation != -1 || !state.IsActive || state.RootUnknown)
            return generation;

        var ultimateOrigin = GetUltimateSourceOrigin(origin);
        if (!ultimateOrigin.InitiallyBound)
            return -1;

        generation = CreateBindingGeneration(ultimateOrigin, ultimateOrigin.BindingPosition);
        state = state.WithGeneration(ultimateOrigin, generation);
        return state.GetGeneration(origin);
    }

    private static bool IsAncestorOrigin(EntityOrigin ancestor, EntityOrigin origin)
    {
        for (
            var current = origin.AliasSourceOrigin;
            current != null;
            current = current.AliasSourceOrigin
        )
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static bool SharesLiveUltimateSource(
        EntityOrigin eventOrigin,
        EntityOrigin candidateOrigin,
        FlowProbeState state
    )
    {
        var eventUltimateSource = GetUltimateSourceOrigin(eventOrigin);
        var candidateUltimateSource = GetUltimateSourceOrigin(candidateOrigin);
        return ReferenceEquals(eventUltimateSource, candidateUltimateSource)
            && state.GetGeneration(eventOrigin) != -1
            && state.GetGeneration(eventOrigin) == state.GetGeneration(eventUltimateSource);
    }

    private static EntityOrigin GetUltimateSourceOrigin(EntityOrigin origin)
    {
        while (origin.AliasSourceOrigin != null)
            origin = origin.AliasSourceOrigin;

        return origin;
    }

    private static long CreateBindingGeneration(EntityOrigin origin, int position)
    {
        return ((long)(uint)position << 32) | (uint)origin.Id;
    }

    private static bool PathCovers(string satisfiedPath, string accessPath)
    {
        return accessPath == satisfiedPath
            || (
                accessPath.Length > satisfiedPath.Length
                && accessPath[satisfiedPath.Length] == '.'
                && accessPath.StartsWith(satisfiedPath, StringComparison.Ordinal)
            );
    }

    private static IEnumerable<BasicBlock> GetSuccessors(BasicBlock block)
    {
        var fallThrough = block.FallThroughSuccessor?.Destination;
        if (fallThrough != null)
            yield return fallThrough;

        var conditional = block.ConditionalSuccessor?.Destination;
        if (conditional != null && !ReferenceEquals(conditional, fallThrough))
            yield return conditional;
    }

    private static void Enqueue(
        BasicBlock block,
        FlowProbeState state,
        Dictionary<int, HashSet<FlowProbeState>> statesByBlock,
        Queue<FlowWorkItem> worklist
    )
    {
        if (!block.IsReachable)
            return;

        if (!statesByBlock.TryGetValue(block.Ordinal, out var states))
        {
            states = new HashSet<FlowProbeState>();
            statesByBlock[block.Ordinal] = states;
        }

        if (states.Add(state))
            worklist.Enqueue(new FlowWorkItem(block, state));
    }

    private sealed class FlowGraphHolder
    {
        public FlowGraphHolder(ControlFlowGraph? graph)
        {
            Graph = graph;
        }

        public ControlFlowGraph? Graph { get; }
    }
}
