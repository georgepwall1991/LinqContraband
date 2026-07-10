using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private sealed class EntityOrigin
    {
        public EntityOrigin(
            int id,
            ILocalSymbol? local,
            bool initiallyBound,
            bool canDetachFromRoot = false,
            bool isIteration = false,
            int bindingPosition = -1,
            bool isUnstableDirectIndex = false,
            EntityOrigin? aliasSourceOrigin = null,
            INamedTypeSymbol? entityType = null,
            string navigationPrefix = ""
        )
        {
            Id = id;
            Local = local;
            InitiallyBound = initiallyBound;
            CanDetachFromRoot = canDetachFromRoot;
            IsIteration = isIteration;
            BindingPosition = bindingPosition;
            IsUnstableDirectIndex = isUnstableDirectIndex;
            AliasSourceOrigin = aliasSourceOrigin;
            EntityType = entityType;
            NavigationPrefix = navigationPrefix;
        }

        public int Id { get; }
        public ILocalSymbol? Local { get; }
        public bool InitiallyBound { get; }
        public bool CanDetachFromRoot { get; }
        public bool IsIteration { get; }
        public int BindingPosition { get; }
        public bool IsUnstableDirectIndex { get; }
        public EntityOrigin? AliasSourceOrigin { get; }
        public INamedTypeSymbol? EntityType { get; }
        public string NavigationPrefix { get; }
    }

    private readonly struct BindingFact : IEquatable<BindingFact>
    {
        public BindingFact(long generation, string path)
        {
            Generation = generation;
            Path = path;
        }

        public long Generation { get; }
        public string Path { get; }

        public bool Equals(BindingFact other)
        {
            return Generation == other.Generation
                && string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is BindingFact other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)(Generation ^ (Generation >> 32)) * 397)
                ^ StringComparer.Ordinal.GetHashCode(Path);
        }
    }

    private sealed class FlowAccessCandidate
    {
        public FlowAccessCandidate(
            int accessId,
            NavigationAccess access,
            EntityOrigin origin,
            bool canDetachFromRoot,
            int bindingPosition,
            bool isAliasBinding,
            ILocalSymbol? accessLocal
        )
        {
            AccessId = accessId;
            Access = access;
            Origin = origin;
            CanDetachFromRoot = canDetachFromRoot;
            BindingPosition = bindingPosition;
            IsAliasBinding = isAliasBinding;
            AccessLocal = accessLocal;
        }

        public int AccessId { get; }
        public NavigationAccess Access { get; }
        public EntityOrigin Origin { get; }
        public bool CanDetachFromRoot { get; }
        public int BindingPosition { get; }
        public bool IsAliasBinding { get; }
        public ILocalSymbol? AccessLocal { get; }
    }

    private enum FlowEventKind
    {
        Materialize,
        BindOrigin,
        BindAliasOrigin,
        BindIterationOrigin,
        UnbindOrigin,
        ReassignRoot,
        EscapeRoot,
        EscapeOrigin,
        CaptureOrigin,
        InvalidateCollection,
        SnapshotOrigin,
        SatisfyPath,
        Access,
    }

    private sealed class FlowEvent
    {
        public FlowEvent(
            FlowEventKind kind,
            SyntaxNode syntax,
            int position,
            EntityOrigin? origin = null,
            string? path = null,
            int accessId = -1,
            EntityOrigin? relatedOrigin = null,
            int sequence = 0,
            int snapshotId = -1,
            bool isFreshIterationStorage = false
        )
        {
            Kind = kind;
            Syntax = syntax;
            Position = position;
            Origin = origin;
            Path = path;
            AccessId = accessId;
            RelatedOrigin = relatedOrigin;
            Sequence = sequence;
            SnapshotId = snapshotId;
            IsFreshIterationStorage = isFreshIterationStorage;
        }

        public FlowEventKind Kind { get; }
        public SyntaxNode Syntax { get; }
        public int Position { get; }
        public EntityOrigin? Origin { get; }
        public string? Path { get; }
        public int AccessId { get; }
        public EntityOrigin? RelatedOrigin { get; }
        public int Sequence { get; }
        public int SnapshotId { get; }
        public bool IsFreshIterationStorage { get; }
    }

    private readonly struct FlowProbeState : IEquatable<FlowProbeState>
    {
        private const string UnboundPrefix = "\u0001";

        public FlowProbeState(
            bool isActive,
            bool rootUnknown,
            bool originBound,
            bool originUnknown,
            bool pathSatisfied,
            bool originIndependentOfRoot,
            bool iterationSourceCaptured,
            bool aliasSourceLinked,
            ImmutableDictionary<int, long>? originGenerations = null,
            ImmutableHashSet<int>? capturedOriginIds = null,
            ImmutableDictionary<int, long>? bindingSnapshots = null,
            ImmutableHashSet<BindingFact>? unknownGenerations = null,
            ImmutableHashSet<BindingFact>? satisfiedGenerations = null,
            ImmutableDictionary<int, string>? originPrefixes = null,
            ImmutableDictionary<int, string>? prefixSnapshots = null
        )
        {
            IsActive = isActive;
            RootUnknown = rootUnknown;
            OriginBound = originBound;
            OriginUnknown = originUnknown;
            PathSatisfied = pathSatisfied;
            OriginIndependentOfRoot = originIndependentOfRoot;
            IterationSourceCaptured = iterationSourceCaptured;
            AliasSourceLinked = aliasSourceLinked;
            OriginGenerations = originGenerations ?? ImmutableDictionary<int, long>.Empty;
            CapturedOriginIds = capturedOriginIds ?? ImmutableHashSet<int>.Empty;
            BindingSnapshots = bindingSnapshots ?? ImmutableDictionary<int, long>.Empty;
            UnknownGenerations = unknownGenerations ?? ImmutableHashSet<BindingFact>.Empty;
            SatisfiedGenerations = satisfiedGenerations ?? ImmutableHashSet<BindingFact>.Empty;
            OriginPrefixes = originPrefixes ?? ImmutableDictionary<int, string>.Empty;
            PrefixSnapshots = prefixSnapshots ?? ImmutableDictionary<int, string>.Empty;
        }

        public bool IsActive { get; }
        public bool RootUnknown { get; }
        public bool OriginBound { get; }
        public bool OriginUnknown { get; }
        public bool PathSatisfied { get; }
        public bool OriginIndependentOfRoot { get; }
        public bool IterationSourceCaptured { get; }
        public bool AliasSourceLinked { get; }
        public ImmutableDictionary<int, long>? OriginGenerations { get; }
        public ImmutableHashSet<int>? CapturedOriginIds { get; }
        public ImmutableDictionary<int, long>? BindingSnapshots { get; }
        public ImmutableHashSet<BindingFact>? UnknownGenerations { get; }
        public ImmutableHashSet<BindingFact>? SatisfiedGenerations { get; }
        public ImmutableDictionary<int, string>? OriginPrefixes { get; }
        public ImmutableDictionary<int, string>? PrefixSnapshots { get; }

        public FlowProbeState WithRootEscape()
        {
            return new FlowProbeState(
                IsActive,
                true,
                OriginBound,
                true,
                PathSatisfied,
                false,
                false,
                false,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithRootReassignment(bool originWasDetached)
        {
            return new FlowProbeState(
                IsActive,
                true,
                OriginBound,
                originWasDetached ? OriginUnknown : true,
                PathSatisfied,
                originWasDetached,
                IterationSourceCaptured,
                false,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithOriginUnknown()
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                true,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithPathSatisfied()
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                true,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithOrigin(
            bool originBound,
            bool originUnknown,
            bool pathSatisfied,
            bool originIndependentOfRoot,
            bool iterationSourceCaptured,
            bool aliasSourceLinked
        )
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                originBound,
                originUnknown,
                pathSatisfied,
                originIndependentOfRoot,
                iterationSourceCaptured,
                aliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithAliasSourceLinked(bool aliasSourceLinked)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                aliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithGeneration(EntityOrigin origin, long generation)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                CurrentGenerations.SetItem(origin.Id, generation),
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public long GetGeneration(EntityOrigin origin)
        {
            for (var current = origin; current != null; current = current.AliasSourceOrigin)
            {
                if (CurrentGenerations.TryGetValue(current.Id, out var generation))
                    return generation;
            }

            return -1;
        }

        public long GetOwnGeneration(EntityOrigin origin)
        {
            return CurrentGenerations.TryGetValue(origin.Id, out var generation) ? generation : -1;
        }

        public FlowProbeState WithCapturedOrigin(EntityOrigin origin)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds.Add(origin.Id),
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithoutCapturedOrigin(EntityOrigin origin)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds.Remove(origin.Id),
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithoutReboundOriginFacts(EntityOrigin origin)
        {
            var generation = GetOwnGeneration(origin);
            if (generation == -1)
                return this;

            var unknownGenerations = CurrentUnknownGenerations;
            foreach (var fact in CurrentUnknownGenerations)
            {
                if (fact.Generation == generation)
                    unknownGenerations = unknownGenerations.Remove(fact);
            }

            var satisfiedGenerations = CurrentSatisfiedGenerations;
            foreach (var fact in CurrentSatisfiedGenerations)
            {
                if (fact.Generation == generation)
                    satisfiedGenerations = satisfiedGenerations.Remove(fact);
            }

            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                unknownGenerations,
                satisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithBindingSnapshot(
            int snapshotId,
            long generation,
            string? navigationPrefix
        )
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots.SetItem(snapshotId, generation),
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots.SetItem(snapshotId, navigationPrefix ?? UnboundPrefix)
            );
        }

        public FlowProbeState WithOriginPrefix(EntityOrigin origin, string? navigationPrefix)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes.SetItem(origin.Id, navigationPrefix ?? UnboundPrefix),
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithUnknownGeneration(long generation, string prefix)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations.Add(new BindingFact(generation, prefix)),
                CurrentSatisfiedGenerations,
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public FlowProbeState WithSatisfiedGeneration(long generation, string path)
        {
            return new FlowProbeState(
                IsActive,
                RootUnknown,
                OriginBound,
                OriginUnknown,
                PathSatisfied,
                OriginIndependentOfRoot,
                IterationSourceCaptured,
                AliasSourceLinked,
                OriginGenerations,
                CurrentCapturedOriginIds,
                CurrentBindingSnapshots,
                CurrentUnknownGenerations,
                CurrentSatisfiedGenerations.Add(new BindingFact(generation, path)),
                CurrentOriginPrefixes,
                CurrentPrefixSnapshots
            );
        }

        public string? GetOriginPrefix(EntityOrigin origin)
        {
            return CurrentOriginPrefixes.TryGetValue(origin.Id, out var prefix)
                ? prefix == UnboundPrefix
                    ? null
                    : prefix
                : origin.NavigationPrefix;
        }

        public string? GetSnapshotPrefix(int snapshotId)
        {
            return CurrentPrefixSnapshots.TryGetValue(snapshotId, out var prefix)
                ? prefix == UnboundPrefix
                    ? null
                    : prefix
                : null;
        }

        public bool IsGenerationUnknown(long generation, string accessPath)
        {
            foreach (var fact in CurrentUnknownGenerations)
            {
                if (
                    fact.Generation == generation
                    && (fact.Path.Length == 0 || PathCovers(fact.Path, accessPath))
                )
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsGenerationSatisfied(long generation, string accessPath)
        {
            foreach (var fact in CurrentSatisfiedGenerations)
            {
                if (fact.Generation == generation && PathCovers(fact.Path, accessPath))
                    return true;
            }

            return false;
        }

        public long GetBindingSnapshot(int snapshotId)
        {
            return CurrentBindingSnapshots.TryGetValue(snapshotId, out var generation)
                ? generation
                : -1;
        }

        public bool IsOriginCaptured(EntityOrigin origin)
        {
            return CurrentCapturedOriginIds.Contains(origin.Id);
        }

        public bool Equals(FlowProbeState other)
        {
            return IsActive == other.IsActive
                && RootUnknown == other.RootUnknown
                && OriginBound == other.OriginBound
                && OriginUnknown == other.OriginUnknown
                && PathSatisfied == other.PathSatisfied
                && OriginIndependentOfRoot == other.OriginIndependentOfRoot
                && IterationSourceCaptured == other.IterationSourceCaptured
                && AliasSourceLinked == other.AliasSourceLinked
                && GenerationsEqual(CurrentGenerations, other.CurrentGenerations)
                && CurrentCapturedOriginIds.SetEquals(other.CurrentCapturedOriginIds)
                && GenerationsEqual(CurrentBindingSnapshots, other.CurrentBindingSnapshots)
                && CurrentUnknownGenerations.SetEquals(other.CurrentUnknownGenerations)
                && CurrentSatisfiedGenerations.SetEquals(other.CurrentSatisfiedGenerations)
                && PrefixesEqual(CurrentOriginPrefixes, other.CurrentOriginPrefixes)
                && PrefixesEqual(CurrentPrefixSnapshots, other.CurrentPrefixSnapshots);
        }

        public override bool Equals(object? obj)
        {
            return obj is FlowProbeState other && Equals(other);
        }

        public override int GetHashCode()
        {
            var value = IsActive ? 1 : 0;
            value = (value * 397) ^ (RootUnknown ? 1 : 0);
            value = (value * 397) ^ (OriginBound ? 1 : 0);
            value = (value * 397) ^ (OriginUnknown ? 1 : 0);
            value = (value * 397) ^ (PathSatisfied ? 1 : 0);
            value = (value * 397) ^ (OriginIndependentOfRoot ? 1 : 0);
            value = (value * 397) ^ (IterationSourceCaptured ? 1 : 0);
            value = (value * 397) ^ (AliasSourceLinked ? 1 : 0);
            foreach (var pair in CurrentGenerations)
                value ^= (pair.Key * 397) ^ (int)(pair.Value ^ (pair.Value >> 32));
            foreach (var originId in CurrentCapturedOriginIds)
                value ^= originId * 7919;
            foreach (var pair in CurrentBindingSnapshots)
                value ^= (pair.Key * 6151) ^ (int)(pair.Value ^ (pair.Value >> 32));
            foreach (var fact in CurrentUnknownGenerations)
                value ^= fact.GetHashCode();
            foreach (var fact in CurrentSatisfiedGenerations)
                value ^= fact.GetHashCode() * 486187739;
            foreach (var pair in CurrentOriginPrefixes)
                value ^= (pair.Key * 3571) ^ StringComparer.Ordinal.GetHashCode(pair.Value);
            foreach (var pair in CurrentPrefixSnapshots)
                value ^= (pair.Key * 4513) ^ StringComparer.Ordinal.GetHashCode(pair.Value);
            return value;
        }

        private ImmutableDictionary<int, long> CurrentGenerations =>
            OriginGenerations ?? ImmutableDictionary<int, long>.Empty;

        private ImmutableHashSet<int> CurrentCapturedOriginIds =>
            CapturedOriginIds ?? ImmutableHashSet<int>.Empty;

        private ImmutableDictionary<int, long> CurrentBindingSnapshots =>
            BindingSnapshots ?? ImmutableDictionary<int, long>.Empty;

        private ImmutableHashSet<BindingFact> CurrentUnknownGenerations =>
            UnknownGenerations ?? ImmutableHashSet<BindingFact>.Empty;

        private ImmutableHashSet<BindingFact> CurrentSatisfiedGenerations =>
            SatisfiedGenerations ?? ImmutableHashSet<BindingFact>.Empty;

        private ImmutableDictionary<int, string> CurrentOriginPrefixes =>
            OriginPrefixes ?? ImmutableDictionary<int, string>.Empty;

        private ImmutableDictionary<int, string> CurrentPrefixSnapshots =>
            PrefixSnapshots ?? ImmutableDictionary<int, string>.Empty;

        private static bool GenerationsEqual(
            ImmutableDictionary<int, long> left,
            ImmutableDictionary<int, long> right
        )
        {
            if (left.Count != right.Count)
                return false;

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            }

            return true;
        }

        private static bool PrefixesEqual(
            ImmutableDictionary<int, string> left,
            ImmutableDictionary<int, string> right
        )
        {
            if (left.Count != right.Count)
                return false;

            foreach (var pair in left)
            {
                if (
                    !right.TryGetValue(pair.Key, out var value)
                    || !string.Equals(pair.Value, value, StringComparison.Ordinal)
                )
                {
                    return false;
                }
            }

            return true;
        }
    }

    private readonly struct FlowWorkItem
    {
        public FlowWorkItem(BasicBlock block, FlowProbeState state)
        {
            Block = block;
            State = state;
        }

        public BasicBlock Block { get; }
        public FlowProbeState State { get; }
    }
}
