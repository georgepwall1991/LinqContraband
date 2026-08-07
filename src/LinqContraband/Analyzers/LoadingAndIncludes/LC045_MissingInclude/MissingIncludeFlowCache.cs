using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// Per-compilation caches for the facts LC045 re-derives once per materializer.
    /// A method with many materializers analyses the same executable root, the same
    /// control-flow graph, and the same block layout once per materializer, so every
    /// fact that depends only on the executable root or the graph is cached here.
    /// </summary>
    private sealed class MissingIncludeFlowCache
    {
        private readonly ConditionalWeakTable<IOperation, FlowGraphHolder> graphs = new();
        private readonly ConditionalWeakTable<IOperation, FlowScopeIndex> scopes = new();
        private readonly ConditionalWeakTable<ControlFlowGraph, BlockOrdinalIndex> blockOrdinals =
            new();

        public bool TryGetGraph(IOperation executableRoot, out FlowGraphHolder holder)
        {
            return graphs.TryGetValue(executableRoot, out holder!);
        }

        public FlowGraphHolder AddGraph(IOperation executableRoot, FlowGraphHolder holder)
        {
            try
            {
                graphs.Add(executableRoot, holder);
                return holder;
            }
            catch (ArgumentException)
            {
                // Concurrent analysis raced us to the same executable root; the winner is
                // an equivalent graph, so reuse it instead of re-creating one.
                return graphs.TryGetValue(executableRoot, out var raced) ? raced : holder;
            }
        }

        public FlowScopeIndex GetScope(IOperation executableRoot)
        {
            return scopes.GetValue(executableRoot, static root => FlowScopeIndex.Create(root));
        }

        public BlockOrdinalIndex GetBlockOrdinals(ControlFlowGraph graph)
        {
            return blockOrdinals.GetValue(graph, static value => BlockOrdinalIndex.Create(value));
        }
    }

    /// <summary>
    /// The operations of one executable root, partitioned by the role LC045's discovery
    /// phases play on them. Building this walk once per root — instead of once per
    /// materializer per phase — is what keeps a method with many materializers linear.
    /// </summary>
    private sealed class FlowScopeIndex
    {
        private FlowScopeIndex(
            ImmutableArray<ScopeOperation> ordered,
            ImmutableArray<IForEachLoopOperation> forEachLoops,
            ImmutableArray<IOperation> aliasBindings,
            ImmutableArray<ILocalFunctionOperation> localFunctions
        )
        {
            Ordered = ordered;
            ForEachLoops = forEachLoops;
            AliasBindings = aliasBindings;
            LocalFunctions = localFunctions;
        }

        /// <summary>
        /// Every owned operation that the event walk can act on, plus every lambda declared
        /// in the root, in source order. Keep the filter in <see cref="IsEventCandidate"/> in
        /// step with the switch in <c>CollectBindingAndEscapeEvents</c> and the navigation
        /// cases in <c>Build</c>.
        /// </summary>
        public ImmutableArray<ScopeOperation> Ordered { get; }

        /// <summary>Owned <c>foreach</c> loops — the only operations origin discovery inspects.</summary>
        public ImmutableArray<IForEachLoopOperation> ForEachLoops { get; }

        /// <summary>
        /// Owned declarators and assignments — the only operations stable-alias discovery
        /// inspects. Keep this in step with the switch in <c>DiscoverStableAliases</c>.
        /// </summary>
        public ImmutableArray<IOperation> AliasBindings { get; }

        /// <summary>Local functions declared directly in the root.</summary>
        public ImmutableArray<ILocalFunctionOperation> LocalFunctions { get; }

        public static FlowScopeIndex Create(IOperation executableRoot)
        {
            var ordered = ImmutableArray.CreateBuilder<ScopeOperation>();
            var forEachLoops = ImmutableArray.CreateBuilder<IForEachLoopOperation>();
            var aliasBindings = ImmutableArray.CreateBuilder<IOperation>();
            var localFunctions = ImmutableArray.CreateBuilder<ILocalFunctionOperation>();

            foreach (var operation in executableRoot.Descendants())
            {
                if (
                    operation is ILocalFunctionOperation localFunction
                    && localFunction.Parent != null
                    && ReferenceEquals(
                        localFunction.Parent.FindOwningExecutableRoot(),
                        executableRoot
                    )
                )
                {
                    localFunctions.Add(localFunction);
                }

                if (
                    operation is IAnonymousFunctionOperation lambda
                    && IsDeclaredInRoot(lambda, executableRoot)
                )
                {
                    ordered.Add(new ScopeOperation(lambda, isDeclaredLambda: true));
                    continue;
                }

                if (!BelongsToRoot(operation, executableRoot))
                    continue;

                if (IsEventCandidate(operation))
                    ordered.Add(new ScopeOperation(operation, isDeclaredLambda: false));

                switch (operation)
                {
                    case IForEachLoopOperation forEach:
                        forEachLoops.Add(forEach);
                        break;
                    case IVariableDeclaratorOperation
                    or ISimpleAssignmentOperation
                    or IDeconstructionAssignmentOperation:
                        aliasBindings.Add(operation);
                        break;
                }
            }

            return new FlowScopeIndex(
                ordered.ToImmutable(),
                forEachLoops.ToImmutable(),
                aliasBindings.ToImmutable(),
                localFunctions.ToImmutable()
            );
        }

        /// <summary>
        /// The operation kinds the event walk reacts to. Everything else is inert, so the
        /// walk skips it instead of paying for it once per materializer.
        /// </summary>
        private static bool IsEventCandidate(IOperation operation)
        {
            return operation
                is IVariableDeclaratorOperation
                    or ISimpleAssignmentOperation
                    or ICoalesceAssignmentOperation
                    or IDeconstructionAssignmentOperation
                    or IInvocationOperation
                    or IObjectCreationOperation
                    or IMethodReferenceOperation
                    or IReturnOperation
                    or IPropertyReferenceOperation
                    or IPropertySubpatternOperation;
        }
    }

    private readonly struct ScopeOperation
    {
        public ScopeOperation(IOperation operation, bool isDeclaredLambda)
        {
            Operation = operation;
            IsDeclaredLambda = isDeclaredLambda;
        }

        public IOperation Operation { get; }

        public bool IsDeclaredLambda { get; }
    }

    /// <summary>
    /// Span-indexed block lookup for one control-flow graph. The linear scan it replaces
    /// ran once per mapped event, so a method with many materializers paid for the whole
    /// graph on every event.
    /// </summary>
    private sealed class BlockOrdinalIndex
    {
        private readonly Dictionary<TextSpan, Entry> smallestContainingSpan;
        private readonly Entry[] reachableByStart;

        private BlockOrdinalIndex(
            Dictionary<TextSpan, Entry> smallestContainingSpan,
            Entry[] reachableByStart
        )
        {
            this.smallestContainingSpan = smallestContainingSpan;
            this.reachableByStart = reachableByStart;
        }

        public static BlockOrdinalIndex Create(ControlFlowGraph graph)
        {
            var smallestContainingSpan = new Dictionary<TextSpan, Entry>();
            var reachable = new List<Entry>();
            var sequence = 0;

            foreach (var block in graph.Blocks)
            {
                foreach (var operation in block.Operations)
                    Consider(operation, block);

                if (block.BranchValue != null)
                    Consider(block.BranchValue, block);
            }

            reachable.Sort(
                static (left, right) =>
                {
                    var startComparison = left.Span.Start.CompareTo(right.Span.Start);
                    return startComparison != 0
                        ? startComparison
                        : left.Sequence.CompareTo(right.Sequence);
                }
            );

            return new BlockOrdinalIndex(smallestContainingSpan, reachable.ToArray());

            void Consider(IOperation operation, BasicBlock block)
            {
                var entry = new Entry(
                    operation.Syntax.Span,
                    operation.Syntax.SyntaxTree,
                    block.Ordinal,
                    sequence++
                );

                // First writer wins: the original scan only replaced its best candidate on a
                // strictly smaller span, so equal spans keep the earliest block.
                if (!smallestContainingSpan.ContainsKey(entry.Span))
                    smallestContainingSpan.Add(entry.Span, entry);

                if (block.IsReachable)
                    reachable.Add(entry);
            }
        }

        /// <summary>
        /// The ordinal of the block holding the smallest operation whose syntax contains
        /// <paramref name="syntax"/>, or -1. Containment in a syntax tree means ancestry, so
        /// walking up from the node finds the smallest containing span directly.
        /// </summary>
        public int FindContainingBlock(SyntaxNode syntax)
        {
            for (var node = syntax; node != null; node = node.Parent)
            {
                if (
                    smallestContainingSpan.TryGetValue(node.Span, out var entry)
                    && entry.Tree == syntax.SyntaxTree
                )
                {
                    return entry.Ordinal;
                }
            }

            return -1;
        }

        /// <summary>
        /// The ordinal of the reachable block holding the earliest operation nested inside
        /// <paramref name="body"/>, or -1.
        /// </summary>
        public int FindFirstBlockInside(SyntaxNode body)
        {
            var bodySpan = body.Span;
            var index = LowerBound(bodySpan.Start);

            for (; index < reachableByStart.Length; index++)
            {
                var entry = reachableByStart[index];
                if (entry.Span.Start > bodySpan.End)
                    break;

                if (entry.Tree != body.SyntaxTree || entry.Span.End > bodySpan.End)
                    continue;

                return entry.Ordinal;
            }

            return -1;
        }

        private int LowerBound(int start)
        {
            var low = 0;
            var high = reachableByStart.Length;

            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (reachableByStart[middle].Span.Start < start)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private readonly struct Entry
        {
            public Entry(TextSpan span, SyntaxTree tree, int ordinal, int sequence)
            {
                Span = span;
                Tree = tree;
                Ordinal = ordinal;
                Sequence = sequence;
            }

            public TextSpan Span { get; }

            public SyntaxTree Tree { get; }

            public int Ordinal { get; }

            public int Sequence { get; }
        }
    }
}
