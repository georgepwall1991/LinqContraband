using System;
using System.Collections.Generic;
using System.Globalization;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private sealed partial class OriginFlowContext
    {
        private void DiscoverStableAliases()
        {
            var candidateLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            var deconstructionAssignments = new Dictionary<ILocalSymbol, List<LocalAssignment>>(
                SymbolEqualityComparer.Default
            );
            foreach (var operation in executableRoot.Descendants())
            {
                if (!BelongsToExecutableRoot(operation))
                    continue;

                switch (operation)
                {
                    case IVariableDeclaratorOperation declarator:
                        candidateLocals.Add(declarator.Symbol);
                        break;
                    case ISimpleAssignmentOperation assignment
                        when assignment.Target.UnwrapConversions()
                            is ILocalReferenceOperation targetLocal:
                        candidateLocals.Add(targetLocal.Local);
                        break;
                    case IDeconstructionAssignmentOperation deconstruction:
                        CollectDeconstructionLocalAssignments(
                            deconstruction.Target,
                            deconstruction.Value,
                            candidateLocals,
                            deconstructionAssignments
                        );
                        break;
                }
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var target in candidateLocals)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (
                        originsByLocal.ContainsKey(target)
                        || SymbolEqualityComparer.Default.Equals(target, resultLocal)
                    )
                    {
                        continue;
                    }

                    var cachedAssignments = LocalAssignmentCache.GetAssignments(
                        executableRoot,
                        target,
                        cancellationToken
                    );
                    IReadOnlyList<LocalAssignment> targetAssignments = cachedAssignments;
                    if (deconstructionAssignments.TryGetValue(target, out var extraAssignments))
                    {
                        var combinedAssignments = new List<LocalAssignment>(
                            cachedAssignments.Count + extraAssignments.Count
                        );
                        combinedAssignments.AddRange(cachedAssignments);
                        combinedAssignments.AddRange(extraAssignments);
                        combinedAssignments.Sort(
                            static (left, right) => left.SpanStart.CompareTo(right.SpanStart)
                        );
                        targetAssignments = combinedAssignments;
                    }

                    if (
                        !TryDescribeLocalBindings(
                            targetAssignments,
                            out var descriptor,
                            out var isStable,
                            out var bindingPosition
                        )
                    )
                    {
                        continue;
                    }

                    CreateOrigin(
                        target,
                        initiallyBound: false,
                        canDetachFromRoot: true,
                        isIteration: false,
                        bindingPosition: bindingPosition,
                        descriptor.EntityType,
                        descriptor.NavigationPrefix,
                        aliasSourceOrigin: descriptor.SourceOrigin
                    );
                    if (isStable)
                    {
                        stableAliasLocals.Add(target);
                        stableAliasBindingPositions[target] = bindingPosition;
                    }

                    changed = true;
                }
            }
        }

        private static void CollectDeconstructionLocalAssignments(
            IOperation target,
            IOperation value,
            HashSet<ILocalSymbol> candidateLocals,
            Dictionary<ILocalSymbol, List<LocalAssignment>> assignments
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
                    CollectDeconstructionLocalAssignments(
                        targetTuple.Elements[index],
                        valueTuple.Elements[index],
                        candidateLocals,
                        assignments
                    );
                }

                return;
            }

            if (target is not ILocalReferenceOperation targetLocal)
                return;

            candidateLocals.Add(targetLocal.Local);
            if (!assignments.TryGetValue(targetLocal.Local, out var localAssignments))
            {
                localAssignments = new List<LocalAssignment>();
                assignments[targetLocal.Local] = localAssignments;
            }

            localAssignments.Add(new LocalAssignment(value.Syntax.SpanStart, value));
        }

        private bool TryDescribeLocalBindings(
            IReadOnlyList<LocalAssignment> assignments,
            out BindingDescriptor descriptor,
            out bool isStable,
            out int bindingPosition
        )
        {
            descriptor = default;
            isStable = false;
            bindingPosition = int.MaxValue;
            var sawTrackedBinding = false;
            var everyAssignmentTracked = assignments.Count > 0;
            var sameSource = true;

            foreach (var assignment in assignments)
            {
                if (!TryResolveUniformBinding(assignment.Value, out var current))
                {
                    everyAssignmentTracked = false;
                    continue;
                }

                bindingPosition = Math.Min(bindingPosition, assignment.Value.Syntax.Span.End);
                if (!sawTrackedBinding)
                {
                    descriptor = current;
                    sawTrackedBinding = true;
                    continue;
                }

                if (!HasSameBindingShape(descriptor, current))
                {
                    everyAssignmentTracked = false;
                    sameSource = false;
                    continue;
                }

                sameSource &= BindingsEqual(descriptor, current);
            }

            if (!sawTrackedBinding)
                return false;

            isStable = everyAssignmentTracked && sameSource;
            return true;
        }

        private bool TryResolveUniformBinding(
            IOperation operation,
            out BindingDescriptor descriptor
        )
        {
            operation = operation.UnwrapConversions();
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    return TryResolveUniformBinding(parenthesized.Operand, out descriptor);

                case IConditionalOperation conditional when conditional.WhenFalse != null:
                    return TryResolveMatchingBindings(
                        conditional.WhenTrue,
                        conditional.WhenFalse,
                        out descriptor
                    );

                case ICoalesceOperation coalesce:
                    return TryResolveMatchingBindings(
                        coalesce.Value,
                        coalesce.WhenNull,
                        out descriptor
                    );

                case ISwitchExpressionOperation switchExpression:
                    descriptor = default;
                    var hasArm = false;
                    foreach (var arm in switchExpression.Arms)
                    {
                        if (!TryResolveUniformBinding(arm.Value, out var armDescriptor))
                            return false;

                        if (!hasArm)
                        {
                            descriptor = armDescriptor;
                            hasArm = true;
                        }
                        else if (!BindingsEqual(descriptor, armDescriptor))
                        {
                            return false;
                        }
                    }

                    return hasArm;

                default:
                    if (
                        TryResolveEntityOrigin(operation, out var origin)
                        && origin.EntityType != null
                    )
                    {
                        descriptor = new BindingDescriptor(
                            origin,
                            origin.EntityType,
                            origin.NavigationPrefix
                        );
                        return true;
                    }

                    descriptor = default;
                    return false;
            }
        }

        private bool TryResolveMatchingBindings(
            IOperation left,
            IOperation right,
            out BindingDescriptor descriptor
        )
        {
            if (
                TryResolveUniformBinding(left, out descriptor)
                && TryResolveUniformBinding(right, out var rightDescriptor)
                && BindingsEqual(descriptor, rightDescriptor)
            )
            {
                return true;
            }

            descriptor = default;
            return false;
        }

        private static bool BindingsEqual(BindingDescriptor left, BindingDescriptor right)
        {
            return ReferenceEquals(
                    GetUltimateSourceOrigin(left.SourceOrigin),
                    GetUltimateSourceOrigin(right.SourceOrigin)
                ) && HasSameBindingShape(left, right);
        }

        private static bool HasSameBindingShape(BindingDescriptor left, BindingDescriptor right)
        {
            return SymbolEqualityComparer.Default.Equals(left.EntityType, right.EntityType)
                && string.Equals(
                    left.NavigationPrefix,
                    right.NavigationPrefix,
                    StringComparison.Ordinal
                );
        }

        private void GetAccessDetachment(
            IPropertyReferenceOperation propertyReference,
            EntityOrigin origin,
            out bool canDetachFromRoot,
            out int bindingPosition,
            out bool isAliasBinding,
            out ILocalSymbol? accessLocal
        )
        {
            canDetachFromRoot = origin.CanDetachFromRoot;
            bindingPosition = origin.BindingPosition;
            isAliasBinding = origin.AliasSourceOrigin != null;
            accessLocal = null;

            if (!TryFindAccessRootLocal(propertyReference, origin, out var rootLocal))
                return;

            accessLocal = rootLocal;
            if (originsByLocal.TryGetValue(rootLocal, out var accessOrigin))
            {
                canDetachFromRoot = accessOrigin.CanDetachFromRoot;
                bindingPosition = accessOrigin.BindingPosition;
                isAliasBinding = accessOrigin.AliasSourceOrigin != null;
            }

            if (stableAliasBindingPositions.TryGetValue(rootLocal, out var aliasBindingPosition))
            {
                canDetachFromRoot = true;
                bindingPosition = aliasBindingPosition;
                isAliasBinding = true;
            }
        }

        private bool TryFindAccessRootLocal(
            IPropertyReferenceOperation propertyReference,
            EntityOrigin origin,
            out ILocalSymbol local
        )
        {
            IOperation? current = propertyReference;
            while (current != null)
            {
                current = current.UnwrapConversions();
                switch (current)
                {
                    case ILocalReferenceOperation localReference
                        when originsByLocal.TryGetValue(localReference.Local, out var localOrigin)
                            && (
                                ReferenceEquals(localOrigin, origin)
                                || IsAncestorOrigin(localOrigin, origin)
                            ):
                        local = localReference.Local;
                        return true;

                    case IPropertyReferenceOperation property:
                        current = property.Instance;
                        if (current?.UnwrapConversions() is IConditionalAccessInstanceOperation)
                            current = ResolveConditionalAccessReceiver(property);
                        continue;

                    case IConditionalAccessOperation conditionalAccess:
                        current = conditionalAccess.Operation;
                        continue;

                    default:
                        local = null!;
                        return false;
                }
            }

            local = null!;
            return false;
        }

        private void DiscoverLocalFunctionCaptures()
        {
            foreach (var operation in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    operation is not ILocalFunctionOperation localFunction
                    || localFunction.Parent == null
                    || !ReferenceEquals(
                        localFunction.Parent.FindOwningExecutableRoot(),
                        executableRoot
                    )
                )
                {
                    continue;
                }

                var capture = new LocalFunctionCapture();
                foreach (var descendant in localFunction.Descendants())
                {
                    if (
                        descendant is not ILocalReferenceOperation localReference
                        || !TryResolveTrackedSource(localReference, out var isRoot, out var origin)
                    )
                    {
                        continue;
                    }

                    if (isRoot)
                        capture.EscapesRoot = true;
                    else if (origin?.IsUnstableDirectIndex == true)
                        capture.EscapesRoot = true;
                    else if (origin != null)
                        capture.OriginIds.Add(origin.Id);
                }

                if (capture.EscapesRoot || capture.OriginIds.Count > 0)
                    localFunctionCaptures[localFunction.Symbol] = capture;
            }
        }

        private static int FindFirstBlockOrdinalInside(ControlFlowGraph graph, SyntaxNode body)
        {
            var bestOrdinal = -1;
            var bestPosition = int.MaxValue;

            foreach (var block in graph.Blocks)
            {
                if (!block.IsReachable)
                    continue;

                foreach (var operation in block.Operations)
                    Consider(operation, block.Ordinal);

                if (block.BranchValue != null)
                    Consider(block.BranchValue, block.Ordinal);
            }

            return bestOrdinal;

            void Consider(IOperation operation, int ordinal)
            {
                if (operation.Syntax.SyntaxTree != body.SyntaxTree)
                    return;

                var span = operation.Syntax.Span;
                var bodySpan = body.Span;
                if (span.Start < bodySpan.Start || span.End > bodySpan.End)
                    return;

                if (span.Start < bestPosition)
                {
                    bestPosition = span.Start;
                    bestOrdinal = ordinal;
                }
            }
        }

        private static string GetDirectIndexOriginKey(IOperation operation, out bool isUnstable)
        {
            operation = operation.UnwrapConversions();
            if (operation is IConditionalAccessOperation conditionalAccess)
                operation = conditionalAccess.WhenNotNull.UnwrapConversions();

            var values = new List<string>();
            switch (operation)
            {
                case IPropertyReferenceOperation propertyReference
                    when propertyReference.Arguments.Length > 0:
                    foreach (var argument in propertyReference.Arguments)
                    {
                        if (!TryFormatConstant(argument.Value, out var value))
                            return SiteKey(operation, out isUnstable);
                        values.Add(value);
                    }
                    break;

                case IArrayElementReferenceOperation arrayElement
                    when arrayElement.Indices.Length > 0:
                    foreach (var index in arrayElement.Indices)
                    {
                        if (!TryFormatConstant(index, out var value))
                            return SiteKey(operation, out isUnstable);
                        values.Add(value);
                    }
                    break;

                default:
                    return SiteKey(operation, out isUnstable);
            }

            isUnstable = false;
            return "constant:" + string.Join("|", values);
        }

        private static bool TryFormatConstant(IOperation operation, out string value)
        {
            value = null!;
            if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value == null)
                return false;

            var constant = operation.ConstantValue.Value;
            value =
                constant.GetType().FullName
                + ":"
                + Convert.ToString(constant, CultureInfo.InvariantCulture);
            return true;
        }

        private static string SiteKey(IOperation operation, out bool isUnstable)
        {
            isUnstable = true;
            return "site:" + operation.Syntax.SpanStart + ":" + operation.Syntax.Span.Length;
        }
    }

    private sealed class IterationBinding
    {
        public IterationBinding(EntityOrigin origin, SyntaxNode body)
        {
            Origin = origin;
            Body = body;
        }

        public EntityOrigin Origin { get; }
        public SyntaxNode Body { get; }
    }

    private readonly struct BindingDescriptor
    {
        public BindingDescriptor(
            EntityOrigin sourceOrigin,
            INamedTypeSymbol entityType,
            string navigationPrefix
        )
        {
            SourceOrigin = sourceOrigin;
            EntityType = entityType;
            NavigationPrefix = navigationPrefix;
        }

        public EntityOrigin SourceOrigin { get; }
        public INamedTypeSymbol EntityType { get; }
        public string NavigationPrefix { get; }
    }

    private sealed class LocalFunctionCapture
    {
        public bool EscapesRoot { get; set; }
        public HashSet<int> OriginIds { get; } = new();
    }
}
