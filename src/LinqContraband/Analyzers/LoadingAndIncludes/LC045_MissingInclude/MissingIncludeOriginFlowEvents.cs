using System.Collections.Generic;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private sealed partial class OriginFlowContext
    {
        private void CollectBindingAndEscapeEvents(IOperation operation)
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when originsByLocal.TryGetValue(declarator.Symbol, out var declaratorOrigin)
                        && declarator.Initializer != null:
                    if (
                        SymbolEqualityComparer.Default.Equals(declarator.Symbol, resultLocal)
                        && IsMaterializerResult(declarator.Initializer.Value)
                    )
                    {
                        break;
                    }

                    AddOriginBindingEvent(
                        declaratorOrigin,
                        declarator.Initializer.Value,
                        declarator.Initializer.Value.Syntax,
                        declarator.Initializer.Value.Syntax.Span.End
                    );
                    break;

                case ISimpleAssignmentOperation assignment:
                    CollectAssignmentEvent(assignment);
                    break;

                case ICoalesceAssignmentOperation coalesceAssignment:
                    CollectCoalesceAssignmentEvent(coalesceAssignment);
                    break;

                case IDeconstructionAssignmentOperation deconstruction:
                    CollectDeconstructionAssignmentEvents(
                        deconstruction.Target,
                        deconstruction.Value,
                        deconstruction.Syntax,
                        deconstruction.Syntax.Span.End
                    );
                    break;

                case IInvocationOperation invocation
                    when !IsMaterializer(invocation)
                        && !IsExactMaterializedCollectionElementExtraction(invocation)
                        && !IsEffectFreeSupportedCollectionCallback(
                            invocation,
                            materializer,
                            resultLocal
                        ):
                    CollectInvocationEscapeEvents(invocation);
                    break;

                case IObjectCreationOperation creation:
                    CollectObjectCreationEscapeEvents(creation);
                    break;

                case IMethodReferenceOperation methodReference:
                    CollectMethodReferenceCaptureEvents(methodReference);
                    break;

                case IReturnOperation returnOperation when returnOperation.ReturnedValue != null:
                    CollectEscapesFromPossibleValue(
                        returnOperation.ReturnedValue,
                        returnOperation.ReturnedValue.Syntax,
                        returnOperation.ReturnedValue.Syntax.Span.End
                    );
                    break;
            }
        }

        private void CollectAssignmentEvent(ISimpleAssignmentOperation assignment)
        {
            var target = assignment.Target.UnwrapConversions();

            if (target is ILocalReferenceOperation targetLocal)
            {
                if (targetLocal.Local.RefKind != RefKind.None)
                {
                    CollectEscapesFromPossibleValue(
                        assignment.Value,
                        assignment.Syntax,
                        assignment.Syntax.Span.End
                    );
                    return;
                }

                if (SymbolEqualityComparer.Default.Equals(targetLocal.Local, resultLocal))
                {
                    if (IsMaterializerResult(assignment.Value))
                        return;

                    events.Add(
                        new FlowEvent(
                            FlowEventKind.ReassignRoot,
                            assignment.Syntax,
                            assignment.Syntax.Span.End,
                            relatedOrigin: rootEntityOrigin
                        )
                    );
                    return;
                }

                if (originsByLocal.TryGetValue(targetLocal.Local, out var targetOrigin))
                {
                    AddOriginBindingEvent(
                        targetOrigin,
                        assignment.Value,
                        assignment.Syntax,
                        assignment.Syntax.Span.End
                    );
                    return;
                }
            }

            if (
                target is IParameterReferenceOperation targetParameter
                && originsByParameter.TryGetValue(
                    targetParameter.Parameter,
                    out var parameterOrigin
                )
            )
            {
                events.Add(
                    new FlowEvent(
                        FlowEventKind.UnbindOrigin,
                        assignment.Syntax,
                        assignment.Syntax.Span.End,
                        parameterOrigin
                    )
                );
                return;
            }

            if (target is not ILocalReferenceOperation)
            {
                if (
                    TryResolveCollectionNavigationEscape(
                        target,
                        out var collectionParentOrigin,
                        out var collectionPath
                    )
                )
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.InvalidateCollection,
                            assignment.Syntax,
                            assignment.Syntax.Span.End,
                            collectionParentOrigin,
                            collectionPath
                        )
                    );
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.SatisfyPath,
                            assignment.Syntax,
                            assignment.Syntax.Span.End,
                            collectionParentOrigin,
                            collectionPath
                        )
                    );
                    return;
                }

                if (
                    TryResolveTrackedSource(target, out var isRoot, out var indexedOrigin)
                    && !isRoot
                    && indexedOrigin != null
                )
                {
                    if (indexedOrigin.IsUnstableDirectIndex)
                    {
                        events.Add(
                            new FlowEvent(
                                FlowEventKind.EscapeRoot,
                                assignment.Syntax,
                                assignment.Syntax.Span.End
                            )
                        );
                    }
                    else
                    {
                        AddOriginBindingEvent(
                            indexedOrigin,
                            assignment.Value,
                            assignment.Syntax,
                            assignment.Syntax.Span.End
                        );
                    }

                    return;
                }

                CollectEscapesFromPossibleValue(
                    assignment.Value,
                    assignment.Syntax,
                    assignment.Syntax.Span.End
                );
            }
        }

        private void CollectCoalesceAssignmentEvent(ICoalesceAssignmentOperation assignment)
        {
            var target = assignment.Target.UnwrapConversions();
            if (target is ILocalReferenceOperation targetLocal)
            {
                if (targetLocal.Local.RefKind != RefKind.None)
                {
                    CollectEscapesFromPossibleValue(
                        assignment.Value,
                        assignment.Syntax,
                        assignment.Syntax.Span.End
                    );
                    return;
                }

                if (SymbolEqualityComparer.Default.Equals(targetLocal.Local, resultLocal))
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.ReassignRoot,
                            assignment.Syntax,
                            assignment.Syntax.Span.End,
                            relatedOrigin: rootEntityOrigin
                        )
                    );
                    return;
                }

                if (originsByLocal.TryGetValue(targetLocal.Local, out var targetOrigin))
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.UnbindOrigin,
                            assignment.Syntax,
                            assignment.Syntax.Span.End,
                            targetOrigin
                        )
                    );
                    return;
                }
            }

            CollectEscapesFromPossibleValue(
                assignment.Value,
                assignment.Syntax,
                assignment.Syntax.Span.End
            );
        }

        private void AddOriginBindingEvent(
            EntityOrigin targetOrigin,
            IOperation value,
            SyntaxNode syntax,
            int position,
            int sequence = 0,
            bool snapshotSource = false
        )
        {
            if (TryResolveUniformBinding(value, out var descriptor))
            {
                var targetDescriptor = new BindingDescriptor(
                    targetOrigin,
                    targetOrigin.EntityType ?? entityType,
                    targetOrigin.NavigationPrefix
                );
                if (
                    !SymbolEqualityComparer.Default.Equals(
                        targetDescriptor.EntityType,
                        descriptor.EntityType
                    )
                )
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.UnbindOrigin,
                            syntax,
                            position,
                            targetOrigin,
                            sequence: sequence
                        )
                    );
                    return;
                }

                var snapshotId = -1;
                if (snapshotSource)
                {
                    snapshotId = nextSnapshotId++;
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.SnapshotOrigin,
                            syntax,
                            position,
                            descriptor.SourceOrigin,
                            sequence: int.MinValue + snapshotId,
                            snapshotId: snapshotId
                        )
                    );
                }

                events.Add(
                    new FlowEvent(
                        FlowEventKind.BindAliasOrigin,
                        syntax,
                        position,
                        targetOrigin,
                        relatedOrigin: descriptor.SourceOrigin,
                        sequence: sequence,
                        snapshotId: snapshotId,
                        isFreshIterationStorage: IsFreshIterationStorage(
                            targetOrigin,
                            descriptor.SourceOrigin,
                            value.Syntax.Span.End
                        )
                    )
                );
                return;
            }

            events.Add(
                new FlowEvent(
                    FlowEventKind.UnbindOrigin,
                    syntax,
                    position,
                    targetOrigin,
                    sequence: sequence
                )
            );
        }

        private bool IsFreshIterationStorage(
            EntityOrigin targetOrigin,
            EntityOrigin sourceOrigin,
            int bindingPosition
        )
        {
            if (targetOrigin.Local == null || targetOrigin.BindingPosition != bindingPosition)
                return false;

            var iterationSource = sourceOrigin;
            while (iterationSource != null && !iterationSource.IsIteration)
                iterationSource = iterationSource.AliasSourceOrigin;

            if (iterationSource == null)
                return false;

            foreach (var binding in iterationBindings)
            {
                if (!ReferenceEquals(binding.Origin, iterationSource))
                    continue;

                foreach (var location in targetOrigin.Local.Locations)
                {
                    if (
                        location.IsInSource
                        && location.SourceTree == binding.Body.SyntaxTree
                        && binding.Body.Span.Contains(location.SourceSpan)
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CollectDeconstructionAssignmentEvents(
            IOperation target,
            IOperation value,
            SyntaxNode syntax,
            int completionPosition
        )
        {
            target = target.UnwrapConversions();
            value = value.UnwrapConversions();

            if (
                target is ITupleOperation targetTuple
                && value is ITupleOperation valueTuple
                && targetTuple.Elements.Length == valueTuple.Elements.Length
            )
            {
                for (var index = 0; index < targetTuple.Elements.Length; index++)
                {
                    CollectDeconstructionAssignmentEvents(
                        targetTuple.Elements[index],
                        valueTuple.Elements[index],
                        syntax,
                        completionPosition
                    );
                }

                return;
            }

            if (target is IDiscardOperation)
                return;

            if (target is ILocalReferenceOperation targetLocal)
            {
                if (SymbolEqualityComparer.Default.Equals(targetLocal.Local, resultLocal))
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.ReassignRoot,
                            syntax,
                            completionPosition,
                            relatedOrigin: rootEntityOrigin,
                            sequence: target.Syntax.SpanStart
                        )
                    );
                }
                else if (originsByLocal.TryGetValue(targetLocal.Local, out var targetOrigin))
                {
                    AddOriginBindingEvent(
                        targetOrigin,
                        value,
                        syntax,
                        completionPosition,
                        sequence: target.Syntax.SpanStart,
                        snapshotSource: true
                    );
                }

                return;
            }

            if (
                TryResolveCollectionNavigationEscape(
                    target,
                    out var collectionParentOrigin,
                    out var collectionPath
                )
            )
            {
                events.Add(
                    new FlowEvent(
                        FlowEventKind.InvalidateCollection,
                        syntax,
                        completionPosition,
                        collectionParentOrigin,
                        collectionPath,
                        sequence: target.Syntax.SpanStart
                    )
                );
                events.Add(
                    new FlowEvent(
                        FlowEventKind.SatisfyPath,
                        syntax,
                        completionPosition,
                        collectionParentOrigin,
                        collectionPath,
                        sequence: target.Syntax.SpanStart
                    )
                );
                return;
            }

            if (
                TryResolveTrackedSource(target, out var isRoot, out var indexedOrigin)
                && !isRoot
                && indexedOrigin != null
            )
            {
                if (indexedOrigin.IsUnstableDirectIndex)
                {
                    events.Add(new FlowEvent(FlowEventKind.EscapeRoot, syntax, completionPosition));
                }
                else
                {
                    AddOriginBindingEvent(
                        indexedOrigin,
                        value,
                        syntax,
                        completionPosition,
                        sequence: target.Syntax.SpanStart,
                        snapshotSource: true
                    );
                }

                return;
            }

            CollectEscapesFromPossibleValue(value, syntax, completionPosition);
        }

        private void CollectEscapesFromPossibleValue(
            IOperation value,
            SyntaxNode syntax,
            int completionPosition
        )
        {
            switch (value)
            {
                case IConversionOperation conversion:
                    CollectEscapesFromPossibleValue(conversion.Operand, syntax, completionPosition);
                    return;

                case IParenthesizedOperation parenthesized:
                    CollectEscapesFromPossibleValue(
                        parenthesized.Operand,
                        syntax,
                        completionPosition
                    );
                    return;

                case IConditionalOperation conditional:
                    CollectEscapesFromPossibleValue(
                        conditional.WhenTrue,
                        syntax,
                        completionPosition
                    );
                    if (conditional.WhenFalse != null)
                    {
                        CollectEscapesFromPossibleValue(
                            conditional.WhenFalse,
                            syntax,
                            completionPosition
                        );
                    }
                    return;

                case ICoalesceOperation coalesce:
                    CollectEscapesFromPossibleValue(coalesce.Value, syntax, completionPosition);
                    CollectEscapesFromPossibleValue(coalesce.WhenNull, syntax, completionPosition);
                    return;

                case ITupleOperation tuple:
                    foreach (var element in tuple.Elements)
                    {
                        CollectEscapesFromPossibleValue(element, syntax, completionPosition);
                    }
                    return;

                case ISwitchExpressionOperation switchExpression:
                    foreach (var arm in switchExpression.Arms)
                    {
                        CollectEscapesFromPossibleValue(arm.Value, syntax, completionPosition);
                    }
                    return;

                case ISimpleAssignmentOperation assignment:
                    CollectEscapesFromPossibleValue(assignment.Value, syntax, completionPosition);
                    return;

                default:
                    AddEscapeForSource(value, syntax, completionPosition);
                    return;
            }
        }

        private void CollectInvocationEscapeEvents(IInvocationOperation invocation)
        {
            CollectCallEscapeEvents(
                invocation.Syntax,
                invocation.Syntax.Span.End,
                invocation.Instance,
                invocation.Arguments,
                invocation.TargetMethod
            );
        }

        private void CollectObjectCreationEscapeEvents(IObjectCreationOperation creation)
        {
            CollectCallEscapeEvents(
                creation.Syntax,
                creation.Initializer?.Syntax.SpanStart ?? creation.Syntax.Span.End,
                instance: null,
                arguments: creation.Arguments,
                targetMethod: null
            );
        }

        private void CollectCallEscapeEvents(
            SyntaxNode syntax,
            int completionPosition,
            IOperation? instance,
            IEnumerable<IArgumentOperation> arguments,
            IMethodSymbol? targetMethod
        )
        {
            var escapedOrigins = new Dictionary<int, List<string?>>();
            var escapedRoot = false;

            AddCallSource(instance);
            foreach (var argument in arguments)
                AddCallSource(argument.Value);

            if (
                targetMethod != null
                && localFunctionCaptures.TryGetValue(targetMethod, out var capture)
            )
            {
                escapedRoot |= capture.EscapesRoot;
                foreach (var originId in capture.OriginIds)
                    AddEscapedOrigin(originId, navigationPath: null);
            }

            if (escapedRoot)
            {
                events.Add(new FlowEvent(FlowEventKind.EscapeRoot, syntax, completionPosition));
            }

            foreach (var escapedOrigin in escapedOrigins)
            {
                var origin = FindOrigin(escapedOrigin.Key);
                if (origin == null)
                    continue;

                foreach (var navigationPath in escapedOrigin.Value)
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.EscapeOrigin,
                            syntax,
                            completionPosition,
                            origin,
                            navigationPath
                        )
                    );
                }
            }

            void AddCallSource(IOperation? source)
            {
                switch (source)
                {
                    case IConversionOperation conversion:
                        AddCallSource(conversion.Operand);
                        return;

                    case IParenthesizedOperation parenthesized:
                        AddCallSource(parenthesized.Operand);
                        return;

                    case IConditionalAccessInstanceOperation conditionalInstance:
                        AddCallSource(ResolveConditionalAccessReceiver(conditionalInstance));
                        return;

                    case IConditionalOperation conditional:
                        AddCallSource(conditional.WhenTrue);
                        AddCallSource(conditional.WhenFalse);
                        return;

                    case ICoalesceOperation coalesce:
                        AddCallSource(coalesce.Value);
                        AddCallSource(coalesce.WhenNull);
                        return;

                    case ITupleOperation tuple:
                        foreach (var element in tuple.Elements)
                            AddCallSource(element);
                        return;

                    case ISwitchExpressionOperation switchExpression:
                        foreach (var arm in switchExpression.Arms)
                            AddCallSource(arm.Value);
                        return;

                    case ISimpleAssignmentOperation assignment:
                        AddCallSource(assignment.Value);
                        return;
                }

                if (
                    !TryResolveEscapeSource(
                        source,
                        out var isRoot,
                        out var origin,
                        out var navigationPath
                    )
                )
                    return;

                if (isRoot)
                    escapedRoot = true;
                else if (origin?.IsUnstableDirectIndex == true)
                    escapedRoot = true;
                else if (origin != null)
                    AddEscapedOrigin(origin.Id, navigationPath);
            }

            void AddEscapedOrigin(int originId, string? navigationPath)
            {
                if (!escapedOrigins.TryGetValue(originId, out var navigationPaths))
                {
                    navigationPaths = new List<string?>();
                    escapedOrigins[originId] = navigationPaths;
                }

                if (navigationPaths.Contains(null))
                    return;

                if (navigationPath == null)
                {
                    navigationPaths.Clear();
                    navigationPaths.Add(null);
                }
                else if (!navigationPaths.Contains(navigationPath))
                {
                    navigationPaths.Add(navigationPath);
                }
            }
        }

        private void CollectLambdaCaptureEvents(IAnonymousFunctionOperation lambda)
        {
            var escapedOrigins = new HashSet<int>();
            var escapedRoot = false;

            foreach (var descendant in lambda.Descendants())
            {
                if (descendant is not ILocalReferenceOperation localReference)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(localReference.Local, resultLocal))
                {
                    escapedRoot = true;
                }
                else if (originsByLocal.TryGetValue(localReference.Local, out var origin))
                {
                    escapedOrigins.Add(origin.Id);
                }
            }

            if (escapedRoot)
            {
                events.Add(
                    new FlowEvent(FlowEventKind.EscapeRoot, lambda.Syntax, lambda.Syntax.Span.End)
                );

                if (materializer != null && lambda.Syntax.Span.End < materializer.Syntax.Span.End)
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.EscapeRoot,
                            materializer.Syntax,
                            materializer.Syntax.Span.End
                        )
                    );
                }
            }

            foreach (var originId in escapedOrigins)
            {
                var origin = FindOrigin(originId);
                if (origin != null)
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.CaptureOrigin,
                            lambda.Syntax,
                            lambda.Syntax.Span.End,
                            origin
                        )
                    );
                }
            }
        }

        private void CollectMethodReferenceCaptureEvents(IMethodReferenceOperation methodReference)
        {
            if (!localFunctionCaptures.TryGetValue(methodReference.Method, out var capture))
                return;

            if (capture.EscapesRoot)
            {
                events.Add(
                    new FlowEvent(
                        FlowEventKind.EscapeRoot,
                        methodReference.Syntax,
                        methodReference.Syntax.Span.End
                    )
                );
            }

            foreach (var originId in capture.OriginIds)
            {
                var origin = FindOrigin(originId);
                if (origin != null)
                {
                    events.Add(
                        new FlowEvent(
                            FlowEventKind.CaptureOrigin,
                            methodReference.Syntax,
                            methodReference.Syntax.Span.End,
                            origin
                        )
                    );
                }
            }
        }

        private void CollectNavigationEvent(IPropertyReferenceOperation propertyReference)
        {
            if (IsInsideNameOf(propertyReference))
                return;

            if (
                !TryGetAccessPath(
                    propertyReference,
                    entityType,
                    entityTypes,
                    TryResolveEntityOrigin,
                    out var path,
                    out var origin
                )
            )
            {
                return;
            }

            if (IsWriteTarget(propertyReference))
            {
                if (IsCollectionNavigation(propertyReference, entityTypes))
                    return;

                events.Add(
                    new FlowEvent(
                        FlowEventKind.SatisfyPath,
                        propertyReference.Syntax,
                        FindWriteCompletionPosition(propertyReference),
                        origin,
                        path,
                        sequence: FindWriteSequence(propertyReference)
                    )
                );
                return;
            }

            if (
                IsCollectionNavigation(propertyReference, entityTypes)
                && IsCollectionMutatorReceiver(propertyReference)
            )
            {
                return;
            }

            var accessId = nextAccessId++;
            var access = new NavigationAccess(path, propertyReference.Syntax);
            GetAccessDetachment(
                propertyReference,
                origin,
                out var canDetachFromRoot,
                out var bindingPosition,
                out var isAliasBinding,
                out var accessLocal
            );
            Candidates.Add(
                new FlowAccessCandidate(
                    accessId,
                    access,
                    origin,
                    canDetachFromRoot,
                    bindingPosition,
                    isAliasBinding,
                    accessLocal
                )
            );
            events.Add(
                new FlowEvent(
                    FlowEventKind.Access,
                    propertyReference.Syntax,
                    propertyReference.Syntax.SpanStart,
                    origin,
                    path,
                    accessId
                )
            );
        }

        private void CollectPropertyPatternNavigationEvent(IPropertySubpatternOperation subpattern)
        {
            if (
                subpattern
                    .ChildOperations.OfType<IPropertyReferenceOperation>()
                    .FirstOrDefault()
                    ?.Property
                    is not IPropertySymbol
                || !TryFindPatternRoot(subpattern, out var isPattern)
                || !TryResolveEntityOrigin(isPattern.Value, out var origin)
            )
            {
                return;
            }

            var properties = new List<IPropertySymbol>();
            for (IOperation? current = subpattern; current != null; current = current.Parent)
            {
                if (
                    current is IPropertySubpatternOperation ancestor
                    && ancestor
                        .ChildOperations.OfType<IPropertyReferenceOperation>()
                        .FirstOrDefault()
                        ?.Property
                        is IPropertySymbol property
                )
                    properties.Add(property);
                if (current is IIsPatternOperation)
                    break;
            }
            properties.Reverse();

            var currentEntity = origin.EntityType ?? entityType;
            var pathSegments = new List<string>();
            foreach (var property in properties)
            {
                if (!IsPropertyOfEntity(property, currentEntity))
                    return;

                if (!TryGetNavigationTarget(property, entityTypes, out var targetEntity, out _))
                {
                    if (pathSegments.Count == 0)
                        return;
                    break;
                }

                pathSegments.Add(property.Name);
                currentEntity = targetEntity;
            }

            if (pathSegments.Count == 0)
                return;

            var path = CombineNavigationPath(
                origin.NavigationPrefix,
                string.Join(".", pathSegments)
            );
            var accessId = nextAccessId++;
            Candidates.Add(
                new FlowAccessCandidate(
                    accessId,
                    new NavigationAccess(path, subpattern.Syntax),
                    origin,
                    origin.CanDetachFromRoot,
                    origin.BindingPosition,
                    isAliasBinding: origin.AliasSourceOrigin != null,
                    accessLocal: null
                )
            );
            events.Add(
                new FlowEvent(
                    FlowEventKind.Access,
                    subpattern.Syntax,
                    subpattern.Syntax.SpanStart,
                    origin,
                    path,
                    accessId
                )
            );
        }

        private static bool TryFindPatternRoot(
            IPropertySubpatternOperation subpattern,
            out IIsPatternOperation isPattern
        )
        {
            for (IOperation? current = subpattern.Parent; current != null; current = current.Parent)
            {
                if (current is IIsPatternOperation match)
                {
                    isPattern = match;
                    return true;
                }
            }

            isPattern = null!;
            return false;
        }

        private static int FindWriteCompletionPosition(
            IPropertyReferenceOperation propertyReference
        )
        {
            for (var current = propertyReference.Parent; current != null; current = current.Parent)
            {
                if (current is IAssignmentOperation assignment)
                    return assignment.Syntax.Span.End;

                if (current is IExpressionStatementOperation or IVariableInitializerOperation)
                    break;
            }

            return propertyReference.Syntax.Span.End;
        }

        private static int FindWriteSequence(IPropertyReferenceOperation propertyReference)
        {
            for (var current = propertyReference.Parent; current != null; current = current.Parent)
            {
                if (current is IDeconstructionAssignmentOperation)
                    return propertyReference.Syntax.SpanStart;

                if (current is IExpressionStatementOperation or IVariableInitializerOperation)
                    break;
            }

            return 0;
        }
    }
}
