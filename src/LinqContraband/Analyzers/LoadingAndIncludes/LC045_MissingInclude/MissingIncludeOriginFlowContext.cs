using System;
using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private sealed partial class OriginFlowContext
    {
        private readonly IOperation executableRoot;
        private readonly IInvocationOperation? materializer;
        private readonly ILocalSymbol? resultLocal;
        private readonly IForEachLoopOperation? rootForEach;
        private readonly IParameterSymbol? rootCallbackParameter;
        private readonly SyntaxNode activationSyntax;
        private readonly bool returnsCollection;
        private readonly INamedTypeSymbol entityType;
        private readonly HashSet<INamedTypeSymbol> entityTypes;
        private readonly CancellationToken cancellationToken;
        private readonly Dictionary<ILocalSymbol, EntityOrigin> originsByLocal = new(
            SymbolEqualityComparer.Default
        );
        private readonly Dictionary<IParameterSymbol, EntityOrigin> originsByParameter = new(
            SymbolEqualityComparer.Default
        );
        private readonly Dictionary<string, EntityOrigin> indexedOrigins = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, EntityOrigin> extractionOrigins = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, EntityOrigin> navigationOrigins = new(
            StringComparer.Ordinal
        );
        private readonly HashSet<ILocalSymbol> stableAliasLocals = new(
            SymbolEqualityComparer.Default
        );
        private readonly Dictionary<ILocalSymbol, int> stableAliasBindingPositions = new(
            SymbolEqualityComparer.Default
        );
        private readonly List<IterationBinding> iterationBindings = new();
        private readonly Dictionary<IMethodSymbol, LocalFunctionCapture> localFunctionCaptures =
            new(SymbolEqualityComparer.Default);
        private readonly List<FlowEvent> events = new();
        private EntityOrigin? rootEntityOrigin;
        private int nextOriginId;
        private int nextAccessId;
        private int nextSnapshotId;

        public OriginFlowContext(
            IOperation executableRoot,
            IInvocationOperation materializer,
            ILocalSymbol resultLocal,
            bool returnsCollection,
            INamedTypeSymbol entityType,
            HashSet<INamedTypeSymbol> entityTypes,
            CancellationToken cancellationToken
        )
        {
            this.executableRoot = executableRoot;
            this.materializer = materializer;
            this.resultLocal = resultLocal;
            activationSyntax = materializer.Syntax;
            this.returnsCollection = returnsCollection;
            this.entityType = entityType;
            this.entityTypes = entityTypes;
            this.cancellationToken = cancellationToken;
        }

        public OriginFlowContext(
            IOperation executableRoot,
            IForEachLoopOperation rootForEach,
            INamedTypeSymbol entityType,
            HashSet<INamedTypeSymbol> entityTypes,
            CancellationToken cancellationToken
        )
        {
            this.executableRoot = executableRoot;
            this.rootForEach = rootForEach;
            activationSyntax = rootForEach.Collection.Syntax;
            returnsCollection = true;
            this.entityType = entityType;
            this.entityTypes = entityTypes;
            this.cancellationToken = cancellationToken;
        }

        public OriginFlowContext(
            IAnonymousFunctionOperation callback,
            IParameterSymbol callbackParameter,
            INamedTypeSymbol entityType,
            HashSet<INamedTypeSymbol> entityTypes,
            CancellationToken cancellationToken
        )
        {
            executableRoot = callback;
            rootCallbackParameter = callbackParameter;
            activationSyntax = callback.Body.Syntax;
            returnsCollection = false;
            this.entityType = entityType;
            this.entityTypes = entityTypes;
            this.cancellationToken = cancellationToken;
        }

        public List<FlowAccessCandidate> Candidates { get; } = new();
        public Dictionary<int, List<FlowEvent>> EventsByBlock { get; } = new();

        public FlowAccessCandidate AddRootProbe(SyntaxNode syntax)
        {
            var candidate = new FlowAccessCandidate(
                nextAccessId++,
                new NavigationAccess(string.Empty, syntax),
                FallbackEntityOrigin,
                canDetachFromRoot: false,
                bindingPosition: activationSyntax.Span.End,
                isAliasBinding: false,
                accessLocal: resultLocal
            );
            events.Add(
                new FlowEvent(
                    FlowEventKind.Access,
                    syntax,
                    syntax.Span.Start,
                    candidate.Origin,
                    candidate.Access.Path,
                    candidate.AccessId
                )
            );
            return candidate;
        }

        public void Build()
        {
            DiscoverOrigins();
            DiscoverStableAliases();
            DiscoverLocalFunctionCaptures();

            events.Add(
                new FlowEvent(
                    FlowEventKind.Materialize,
                    activationSyntax,
                    rootCallbackParameter != null
                        ? activationSyntax.Span.Start
                        : activationSyntax.Span.End
                )
            );

            foreach (var operation in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (
                    operation is IAnonymousFunctionOperation lambda
                    && IsDeclaredInExecutableRoot(lambda)
                )
                {
                    CollectLambdaCaptureEvents(lambda);
                    continue;
                }

                if (!BelongsToExecutableRoot(operation))
                    continue;

                CollectBindingAndEscapeEvents(operation);

                if (operation is IPropertyReferenceOperation propertyReference)
                    CollectNavigationEvent(propertyReference);
                else if (operation is IPropertySubpatternOperation propertySubpattern)
                    CollectPropertyPatternNavigationEvent(propertySubpattern);
            }
        }

        public bool TryMapEventsToBlocks(ControlFlowGraph graph)
        {
            foreach (var flowEvent in events)
            {
                var blockOrdinal = FindBlockOrdinal(graph, flowEvent.Syntax);
                if (
                    blockOrdinal < 0
                    && flowEvent.Kind == FlowEventKind.Materialize
                    && executableRoot is IAnonymousFunctionOperation
                )
                {
                    blockOrdinal = FindFirstBlockOrdinalInside(graph, activationSyntax);
                }
                if (blockOrdinal < 0)
                    return false;

                if (!EventsByBlock.TryGetValue(blockOrdinal, out var blockEvents))
                {
                    blockEvents = new List<FlowEvent>();
                    EventsByBlock[blockOrdinal] = blockEvents;
                }

                blockEvents.Add(flowEvent);
            }

            foreach (var binding in iterationBindings)
            {
                var blockOrdinal = FindFirstBlockOrdinalInside(graph, binding.Body);
                if (blockOrdinal < 0)
                    return false;

                if (!EventsByBlock.TryGetValue(blockOrdinal, out var blockEvents))
                {
                    blockEvents = new List<FlowEvent>();
                    EventsByBlock[blockOrdinal] = blockEvents;
                }

                blockEvents.Add(
                    new FlowEvent(
                        FlowEventKind.BindIterationOrigin,
                        binding.Body,
                        int.MinValue,
                        binding.Origin
                    )
                );
            }

            foreach (var blockEvents in EventsByBlock.Values)
            {
                blockEvents.Sort(
                    static (left, right) =>
                    {
                        var positionComparison = left.Position.CompareTo(right.Position);
                        if (positionComparison != 0)
                            return positionComparison;

                        var sequenceComparison = left.Sequence.CompareTo(right.Sequence);
                        return sequenceComparison != 0
                            ? sequenceComparison
                            : left.Kind.CompareTo(right.Kind);
                    }
                );
            }

            return true;
        }

        private void DiscoverOrigins()
        {
            if (rootCallbackParameter != null)
            {
                rootEntityOrigin = CreateParameterOrigin(
                    rootCallbackParameter,
                    initiallyBound: true,
                    activationSyntax.Span.Start,
                    entityType,
                    navigationPrefix: ""
                );
            }

            if (!returnsCollection && resultLocal != null)
            {
                rootEntityOrigin = CreateOrigin(
                    resultLocal,
                    initiallyBound: true,
                    canDetachFromRoot: false,
                    isIteration: false,
                    activationSyntax.Span.End,
                    entityType,
                    navigationPrefix: ""
                );
            }

            if (rootForEach != null)
            {
                AddIterationOrigins(
                    rootForEach,
                    entityType,
                    navigationPrefix: "",
                    aliasSourceOrigin: null
                );
            }

            foreach (var operation in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!BelongsToExecutableRoot(operation))
                    continue;

                switch (operation)
                {
                    case IForEachLoopOperation forEach when IsResultCollection(forEach.Collection):
                        AddIterationOrigins(
                            forEach,
                            entityType,
                            navigationPrefix: "",
                            aliasSourceOrigin: null
                        );
                        break;

                    case IForEachLoopOperation forEach
                        when TryResolveCollectionNavigationIteration(
                            forEach.Collection,
                            out var parentOrigin,
                            out var elementEntityType,
                            out var navigationPrefix
                        ):
                        AddIterationOrigins(
                            forEach,
                            elementEntityType,
                            navigationPrefix,
                            parentOrigin
                        );
                        break;
                }
            }
        }

        private void AddIterationOrigins(
            IForEachLoopOperation forEach,
            INamedTypeSymbol originEntityType,
            string navigationPrefix,
            EntityOrigin? aliasSourceOrigin
        )
        {
            foreach (var local in forEach.Locals)
            {
                var origin = CreateOrigin(
                    local,
                    initiallyBound: false,
                    canDetachFromRoot: true,
                    isIteration: true,
                    forEach.Body.Syntax.SpanStart,
                    originEntityType,
                    navigationPrefix,
                    aliasSourceOrigin
                );
                iterationBindings.Add(new IterationBinding(origin, forEach.Body.Syntax));
            }
        }

        private bool TryResolveCollectionNavigationIteration(
            IOperation collection,
            out EntityOrigin parentOrigin,
            out INamedTypeSymbol elementEntityType,
            out string navigationPrefix
        )
        {
            parentOrigin = null!;
            elementEntityType = null!;
            navigationPrefix = null!;

            var unwrapped = collection.UnwrapConversions();
            if (
                unwrapped is not IPropertyReferenceOperation propertyReference
                || !TryResolveEntityOrigin(propertyReference.Instance, out parentOrigin)
                || !TryGetNavigationTarget(
                    propertyReference.Property,
                    entityTypes,
                    out elementEntityType,
                    out var isCollection
                )
                || !isCollection
                || !IsPropertyOfEntity(
                    propertyReference.Property,
                    parentOrigin.EntityType ?? entityType
                )
            )
            {
                parentOrigin = null!;
                elementEntityType = null!;
                navigationPrefix = null!;
                return false;
            }

            navigationPrefix = CombineNavigationPath(
                parentOrigin.NavigationPrefix,
                propertyReference.Property.Name
            );
            return true;
        }

        private bool TryResolveEntityOrigin(IOperation? operation, out EntityOrigin origin)
        {
            if (TryResolveExactCollectionElementExtraction(operation, out origin))
                return true;

            if (
                TryResolveTrackedSource(operation, out var isRoot, out var trackedOrigin)
                && !isRoot
                && trackedOrigin != null
            )
            {
                origin = trackedOrigin;
                return true;
            }

            var unwrapped = operation?.UnwrapConversions();
            if (
                unwrapped is IPropertyReferenceOperation propertyReference
                && TryResolveEntityOrigin(propertyReference.Instance, out var parentOrigin)
                && TryGetNavigationTarget(
                    propertyReference.Property,
                    entityTypes,
                    out var targetEntity,
                    out var isCollection
                )
                && !isCollection
                && IsPropertyOfEntity(
                    propertyReference.Property,
                    parentOrigin.EntityType ?? entityType
                )
            )
            {
                var prefix = CombineNavigationPath(
                    parentOrigin.NavigationPrefix,
                    propertyReference.Property.Name
                );
                origin = GetOrCreateNavigationOrigin(parentOrigin, targetEntity, prefix);
                return true;
            }

            origin = null!;
            return false;
        }

        private bool TryResolveCollectionNavigationEscape(
            IOperation? operation,
            out EntityOrigin parentOrigin,
            out string navigationPath
        )
        {
            parentOrigin = null!;
            navigationPath = null!;
            var unwrapped = operation?.UnwrapConversions();
            if (
                unwrapped is not IPropertyReferenceOperation propertyReference
                || !TryResolveEntityOrigin(propertyReference.Instance, out parentOrigin)
                || !TryGetNavigationTarget(
                    propertyReference.Property,
                    entityTypes,
                    out _,
                    out var isCollection
                )
                || !isCollection
                || !IsPropertyOfEntity(
                    propertyReference.Property,
                    parentOrigin.EntityType ?? entityType
                )
            )
            {
                parentOrigin = null!;
                navigationPath = null!;
                return false;
            }

            navigationPath = CombineNavigationPath(
                parentOrigin.NavigationPrefix,
                propertyReference.Property.Name
            );
            return true;
        }

        private bool TryResolveEscapeSource(
            IOperation? source,
            out bool isRoot,
            out EntityOrigin? origin,
            out string? navigationPath
        )
        {
            navigationPath = null;
            if (TryResolveTrackedSource(source, out isRoot, out origin))
                return true;

            if (TryResolveEntityOrigin(source, out var entityOrigin))
            {
                isRoot = false;
                origin = entityOrigin;
                return true;
            }

            if (
                TryResolveCollectionNavigationEscape(
                    source,
                    out var parentOrigin,
                    out navigationPath
                )
            )
            {
                isRoot = false;
                origin = parentOrigin;
                return true;
            }

            isRoot = false;
            origin = null;
            return false;
        }

        private bool TryResolveTrackedSource(
            IOperation? operation,
            out bool isRoot,
            out EntityOrigin? origin
        )
        {
            isRoot = false;
            origin = null;
            if (operation == null)
                return false;

            var unwrapped = operation.UnwrapConversions();
            if (unwrapped is ILocalReferenceOperation localReference)
            {
                if (
                    resultLocal != null
                    && SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal)
                )
                {
                    if (returnsCollection)
                    {
                        isRoot = true;
                    }
                    else
                    {
                        origin = rootEntityOrigin;
                    }

                    return true;
                }

                if (originsByLocal.TryGetValue(localReference.Local, out origin))
                    return true;
            }

            if (
                unwrapped is IParameterReferenceOperation parameterReference
                && originsByParameter.TryGetValue(parameterReference.Parameter, out origin)
            )
            {
                return true;
            }

            if (
                returnsCollection
                && resultLocal != null
                && IsIndexedAccessOf(unwrapped, resultLocal)
            )
            {
                var key = GetDirectIndexOriginKey(unwrapped, out var isUnstable);
                if (!indexedOrigins.TryGetValue(key, out origin))
                {
                    origin = new EntityOrigin(
                        nextOriginId++,
                        null,
                        initiallyBound: true,
                        canDetachFromRoot: false,
                        isIteration: false,
                        bindingPosition: unwrapped.Syntax.Span.End,
                        isUnstableDirectIndex: isUnstable,
                        entityType: entityType,
                        navigationPrefix: ""
                    );
                    indexedOrigins[key] = origin;
                }

                return true;
            }

            return false;
        }

        private bool TryResolveExactCollectionElementExtraction(
            IOperation? operation,
            out EntityOrigin origin
        )
        {
            origin = null!;
            if (
                operation?.UnwrapConversions() is not IInvocationOperation invocation
                || !IsExactMaterializedCollectionElementExtraction(invocation)
            )
            {
                return false;
            }

            var key = SiteKey(invocation, out _);
            if (!extractionOrigins.TryGetValue(key, out origin))
            {
                origin = new EntityOrigin(
                    nextOriginId++,
                    local: null,
                    initiallyBound: true,
                    canDetachFromRoot: false,
                    isIteration: false,
                    bindingPosition: invocation.Syntax.Span.End,
                    entityType: entityType,
                    navigationPrefix: ""
                );
                extractionOrigins[key] = origin;
            }

            return true;
        }

        private bool IsExactMaterializedCollectionElementExtraction(IInvocationOperation invocation)
        {
            return IsExactCollectionElementExtraction(invocation)
                && GetQuerySource(invocation) is { } source
                && TryResolveTrackedSource(source, out var isRoot, out _)
                && isRoot;
        }

        private void AddEscapeForSource(IOperation source, SyntaxNode syntax, int position)
        {
            if (
                !TryResolveEscapeSource(
                    source,
                    out var isRoot,
                    out var origin,
                    out var navigationPath
                )
            )
                return;

            if (origin?.IsUnstableDirectIndex == true)
            {
                isRoot = true;
                origin = null;
            }

            events.Add(
                new FlowEvent(
                    isRoot ? FlowEventKind.EscapeRoot : FlowEventKind.EscapeOrigin,
                    syntax,
                    position,
                    origin,
                    navigationPath
                )
            );
        }

        private EntityOrigin CreateOrigin(
            ILocalSymbol local,
            bool initiallyBound,
            bool canDetachFromRoot,
            bool isIteration,
            int bindingPosition,
            INamedTypeSymbol originEntityType,
            string navigationPrefix,
            EntityOrigin? aliasSourceOrigin = null
        )
        {
            if (originsByLocal.TryGetValue(local, out var existing))
                return existing;

            var origin = new EntityOrigin(
                nextOriginId++,
                local,
                initiallyBound,
                canDetachFromRoot,
                isIteration,
                bindingPosition,
                aliasSourceOrigin: aliasSourceOrigin,
                entityType: originEntityType,
                navigationPrefix: navigationPrefix
            );
            originsByLocal[local] = origin;
            return origin;
        }

        private EntityOrigin CreateParameterOrigin(
            IParameterSymbol parameter,
            bool initiallyBound,
            int bindingPosition,
            INamedTypeSymbol originEntityType,
            string navigationPrefix
        )
        {
            if (originsByParameter.TryGetValue(parameter, out var existing))
                return existing;

            var origin = new EntityOrigin(
                nextOriginId++,
                local: null,
                initiallyBound,
                canDetachFromRoot: false,
                isIteration: true,
                bindingPosition,
                entityType: originEntityType,
                navigationPrefix: navigationPrefix
            );
            originsByParameter[parameter] = origin;
            return origin;
        }

        private EntityOrigin? FindOrigin(int id)
        {
            foreach (var origin in originsByLocal.Values)
            {
                if (origin.Id == id)
                    return origin;
            }

            foreach (var origin in originsByParameter.Values)
            {
                if (origin.Id == id)
                    return origin;
            }

            foreach (var origin in indexedOrigins.Values)
            {
                if (origin.Id == id)
                    return origin;
            }

            foreach (var origin in extractionOrigins.Values)
            {
                if (origin.Id == id)
                    return origin;
            }

            foreach (var origin in navigationOrigins.Values)
            {
                if (origin.Id == id)
                    return origin;
            }

            return null;
        }

        private EntityOrigin GetOrCreateNavigationOrigin(
            EntityOrigin parentOrigin,
            INamedTypeSymbol targetEntity,
            string prefix
        )
        {
            var key = parentOrigin.Id + ":" + prefix;
            if (navigationOrigins.TryGetValue(key, out var existing))
                return existing;

            var origin = new EntityOrigin(
                nextOriginId++,
                local: null,
                initiallyBound: false,
                canDetachFromRoot: true,
                isIteration: false,
                bindingPosition: parentOrigin.BindingPosition,
                aliasSourceOrigin: parentOrigin,
                entityType: targetEntity,
                navigationPrefix: prefix
            );
            navigationOrigins[key] = origin;
            return origin;
        }

        private bool IsResultCollection(IOperation operation)
        {
            return resultLocal != null
                && operation.UnwrapConversions() is ILocalReferenceOperation localReference
                && SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal);
        }

        private bool IsMaterializer(IInvocationOperation invocation)
        {
            return materializer != null
                && invocation.Syntax.SyntaxTree == materializer.Syntax.SyntaxTree
                && invocation.Syntax.Span == materializer.Syntax.Span;
        }

        private bool IsMaterializerResult(IOperation operation)
        {
            while (true)
            {
                switch (operation)
                {
                    case IConversionOperation conversion:
                        operation = conversion.Operand;
                        continue;
                    case IParenthesizedOperation parenthesized:
                        operation = parenthesized.Operand;
                        continue;
                    case IAwaitOperation awaitOperation:
                        operation = awaitOperation.Operation;
                        continue;
                    case IInvocationOperation configureAwait
                        when IsFrameworkConfigureAwait(configureAwait)
                            && configureAwait.Instance != null:
                        operation = configureAwait.Instance;
                        continue;
                    default:
                        return operation is IInvocationOperation invocation
                            && materializer != null
                            && IsMaterializer(invocation);
                }
            }
        }

        private bool IsDeclaredInExecutableRoot(IAnonymousFunctionOperation lambda)
        {
            if (executableRoot is IAnonymousFunctionOperation callback)
                return !ReferenceEquals(lambda, callback) && BelongsToExecutableRoot(lambda);

            var parent = lambda.Parent;
            return parent != null
                && ReferenceEquals(parent.FindOwningExecutableRoot(), executableRoot);
        }

        private bool BelongsToExecutableRoot(IOperation operation)
        {
            if (executableRoot is IAnonymousFunctionOperation callback)
            {
                if (
                    operation.Syntax.SyntaxTree != callback.Syntax.SyntaxTree
                    || !callback.Body.Syntax.Span.Contains(operation.Syntax.Span)
                )
                {
                    return false;
                }

                for (
                    var parent = operation.Parent;
                    parent != null && !ReferenceEquals(parent, callback);
                    parent = parent.Parent
                )
                {
                    if (parent is IAnonymousFunctionOperation)
                        return false;
                }

                return true;
            }

            return ReferenceEquals(operation.FindOwningExecutableRoot(), executableRoot);
        }

        private static int FindBlockOrdinal(ControlFlowGraph graph, SyntaxNode syntax)
        {
            var bestOrdinal = -1;
            var bestLength = int.MaxValue;

            foreach (var block in graph.Blocks)
            {
                foreach (var operation in block.Operations)
                    Consider(operation, block.Ordinal);

                if (block.BranchValue != null)
                    Consider(block.BranchValue, block.Ordinal);
            }

            return bestOrdinal;

            void Consider(IOperation operation, int ordinal)
            {
                if (operation.Syntax.SyntaxTree != syntax.SyntaxTree)
                    return;

                var operationSpan = operation.Syntax.Span;
                var targetSpan = syntax.Span;
                if (operationSpan.Start > targetSpan.Start || operationSpan.End < targetSpan.End)
                    return;

                if (operationSpan.Length < bestLength)
                {
                    bestLength = operationSpan.Length;
                    bestOrdinal = ordinal;
                }
            }
        }
    }
}
