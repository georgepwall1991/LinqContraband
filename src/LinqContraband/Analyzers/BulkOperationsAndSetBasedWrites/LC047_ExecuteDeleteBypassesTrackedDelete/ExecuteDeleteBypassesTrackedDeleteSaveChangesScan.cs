using System;
using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    internal readonly struct ConversionScan
    {
        public ConversionScan(
            bool readsDeleted,
            bool convertsState,
            IReadOnlyCollection<INamedTypeSymbol> entityTypes,
            string? singleBoolTrueProperty,
            bool hasPropertyWrite)
        {
            ReadsDeleted = readsDeleted;
            ConvertsState = convertsState;
            EntityTypes = entityTypes;
            SingleBoolTrueProperty = singleBoolTrueProperty;
            HasPropertyWrite = hasPropertyWrite;
        }

        public bool ReadsDeleted { get; }
        public bool ConvertsState { get; }
        public IReadOnlyCollection<INamedTypeSymbol> EntityTypes { get; }
        public string? SingleBoolTrueProperty { get; }
        public bool HasPropertyWrite { get; }
        public bool IsConversion => ReadsDeleted && (ConvertsState || SingleBoolTrueProperty != null || HasPropertyWrite);
    }

    private ConversionScan ScanTypeMethods(
        INamedTypeSymbol type,
        bool isInterceptor,
        CancellationToken cancellationToken)
    {
        var aggregate = new ConversionAccumulator();
        var visited = new MethodDominanceSet();

        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            if (!IsPipelineMethod(method, isInterceptor))
                continue;

            ScanMethodTree(method, type, visited, 0, aggregate, dominatedEntries: null, cancellationToken);
        }

        return aggregate.ToScan();
    }

    private void ScanMethodTree(
        IMethodSymbol method,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        CancellationToken cancellationToken)
    {
        method = method.OriginalDefinition;
        if (depth > 4 || !visited.Add(method, DominatedParameterMask(method, dominatedEntries)))
            return;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = reference.GetSyntax(cancellationToken);
            if (syntax is not (MethodDeclarationSyntax or LocalFunctionStatementSyntax))
                continue;

            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation == null)
                continue;

            WalkConversionOperations(
                operation,
                owningType,
                visited,
                depth,
                aggregate,
                dominatedEntries,
                isScanRoot: true,
                cancellationToken);
        }
    }

    private static bool IsPipelineMethod(IMethodSymbol method, bool isInterceptor)
    {
        if (isInterceptor)
        {
            return method.Name is "SavingChanges" or "SavingChangesAsync";
        }

        return method.Name is "SaveChanges" or "SaveChangesAsync";
    }

    private void WalkConversionOperations(
        IOperation? operation,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        bool isScanRoot,
        CancellationToken cancellationToken)
    {
        if (operation == null)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        switch (operation)
        {
            case ILocalFunctionOperation localFunction:
                if (isScanRoot)
                {
                    WalkConversionOperations(
                        localFunction.Body,
                        owningType,
                        visited,
                        depth,
                        aggregate,
                        dominatedEntries,
                        isScanRoot: false,
                        cancellationToken);
                }

                return;

            case IAnonymousFunctionOperation:
                return;

            case IBlockOperation block:
                WalkBlock(
                    block,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    dominatedEntries,
                    cancellationToken);
                return;

            case IConditionalOperation conditional:
                WalkConditional(
                    conditional,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    dominatedEntries,
                    cancellationToken);
                return;

            case ISwitchOperation switchOperation:
                WalkSwitch(
                    switchOperation,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    dominatedEntries,
                    cancellationToken);
                return;
        }

        ObserveOperation(operation, owningType, visited, depth, aggregate, dominatedEntries, cancellationToken);

        foreach (var child in operation.ChildOperations)
        {
            WalkConversionOperations(
                child,
                owningType,
                visited,
                depth,
                aggregate,
                dominatedEntries,
                isScanRoot: false,
                cancellationToken);
        }
    }

    private void WalkBlock(
        IBlockOperation block,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        CancellationToken cancellationToken)
    {
        var dominate = dominatedEntries;
        var allowSequentialDominance = !BlockHasGoto(block);
        foreach (var statement in block.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (statement is IConditionalOperation conditional)
            {
                if (WalkConditional(
                        conditional,
                        owningType,
                        visited,
                        depth,
                        aggregate,
                        dominate,
                        cancellationToken) is { } sequentiallyDominated &&
                    allowSequentialDominance)
                {
                    dominate = AddDominated(dominate, sequentiallyDominated);
                }

                continue;
            }

            WalkConversionOperations(
                statement,
                owningType,
                visited,
                depth,
                aggregate,
                dominate,
                isScanRoot: false,
                cancellationToken);
        }
    }

    private ISymbol? WalkConditional(
        IConditionalOperation conditional,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        CancellationToken cancellationToken)
    {
        WalkConversionOperations(
            conditional.Condition,
            owningType,
            visited,
            depth,
            aggregate,
            dominatedEntries,
            isScanRoot: false,
            cancellationToken);

        if (TryClassifyDeletedCondition(conditional.Condition, owningType, out var thenEntry, out var elseEntry))
        {
            WalkConversionOperations(
                conditional.WhenTrue,
                owningType,
                visited,
                depth,
                aggregate,
                AddDominated(dominatedEntries, thenEntry),
                isScanRoot: false,
                cancellationToken);
            WalkConversionOperations(
                conditional.WhenFalse,
                owningType,
                visited,
                depth,
                aggregate,
                AddDominated(dominatedEntries, elseEntry),
                isScanRoot: false,
                cancellationToken);
            return elseEntry != null &&
                   conditional.WhenFalse is null &&
                   IsUnconditionalExit(conditional.WhenTrue)
                ? elseEntry
                : null;
        }

        WalkConversionOperations(
            conditional.WhenTrue,
            owningType,
            visited,
            depth,
            aggregate,
            dominatedEntries,
            isScanRoot: false,
            cancellationToken);
        WalkConversionOperations(
            conditional.WhenFalse,
            owningType,
            visited,
            depth,
            aggregate,
            dominatedEntries,
            isScanRoot: false,
            cancellationToken);
        return null;
    }

    private void WalkSwitch(
        ISwitchOperation switchOperation,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        CancellationToken cancellationToken)
    {
        WalkConversionOperations(
            switchOperation.Value,
            owningType,
            visited,
            depth,
            aggregate,
            dominatedEntries,
            isScanRoot: false,
            cancellationToken);

        foreach (var switchCase in switchOperation.Cases)
        {
            var caseDominates = AddDominated(
                dominatedEntries,
                IsStateProperty(switchOperation.Value) && CaseIncludesDeleted(switchCase)
                    ? TryGetEntrySymbol(switchOperation.Value)
                    : null);
            foreach (var clause in switchCase.Clauses)
            {
                WalkConversionOperations(
                    clause,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    dominatedEntries,
                    isScanRoot: false,
                    cancellationToken);
            }

            foreach (var statement in switchCase.Body)
            {
                WalkConversionOperations(
                    statement,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    caseDominates,
                    isScanRoot: false,
                    cancellationToken);
            }
        }
    }

    private void ObserveOperation(
        IOperation operation,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        HashSet<ISymbol>? dominatedEntries,
        CancellationToken cancellationToken)
    {
        if (IsEntityStateMember(operation, "Deleted"))
            aggregate.ReadsDeleted = true;

        if (operation is IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Name == "Entries")
            {
                if (invocation.TargetMethod.TypeArguments.Length == 1 &&
                    invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol typed)
                {
                    aggregate.TypedEntities.Add(typed);
                }
                else if (invocation.TargetMethod.TypeArguments.Length == 0)
                {
                    aggregate.SawUntypedEntries = true;
                }
            }

            var target = invocation.TargetMethod.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(target.ContainingType, owningType))
            {
                ScanMethodTree(
                    target,
                    owningType,
                    visited,
                    depth + 1,
                    aggregate,
                    MapDominatedArguments(invocation, target, dominatedEntries),
                    cancellationToken);
            }
        }

        if (operation is IConversionOperation conversion &&
            dominatedEntries != null &&
            conversion.Type is INamedTypeSymbol convertedType &&
            IsEntityProperty(conversion.Operand) &&
            TryGetEntrySymbol(conversion.Operand) is { } convertedEntry &&
            dominatedEntries.Contains(convertedEntry))
        {
            aggregate.NarrowedEntities.Add(convertedType);
        }

        if (operation is IAssignmentOperation assignment &&
            dominatedEntries != null &&
            TryGetEntrySymbol(assignment.Target) is { } assignedEntry &&
            dominatedEntries.Contains(assignedEntry))
        {
            RecordAssignment(assignment, aggregate);
        }
    }

    private bool TryClassifyDeletedCondition(
        IOperation? condition,
        INamedTypeSymbol owningType,
        out ISymbol? thenEntry,
        out ISymbol? elseEntry)
    {
        thenEntry = null;
        elseEntry = null;
        condition = condition?.UnwrapConversions();

        var negated = false;
        while (condition is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } notOperation)
        {
            negated = !negated;
            condition = notOperation.Operand?.UnwrapConversions();
        }

        if (condition is IBinaryOperation binary &&
            TryGetDeletedComparisonPolarity(binary, out var isNegated, out var testedEntry))
        {
            if (isNegated != negated)
                elseEntry = testedEntry;
            else
                thenEntry = testedEntry;
            return true;
        }

        if (condition is IIsPatternOperation isPattern &&
            IsStateProperty(isPattern.Value) &&
            TryGetDeletedPatternPolarity(isPattern.Pattern, out var patternNegated))
        {
            if (patternNegated != negated)
                elseEntry = TryGetEntrySymbol(isPattern.Value);
            else
                thenEntry = TryGetEntrySymbol(isPattern.Value);
            return true;
        }

        if (condition is IInvocationOperation invocation &&
            TryClassifyHelperDeletedPredicate(invocation, owningType, out var helperNegated, out var helperEntry))
        {
            if (helperNegated != negated)
                elseEntry = helperEntry;
            else
                thenEntry = helperEntry;
            return true;
        }

        return false;
    }

    // The tested argument must be a plain local/parameter reference so the dominated
    // entry can be linked to the entry converted under dominance. Field-rooted
    // arguments stay quiet for helper predicates even though the direct path links
    // fields, because a field argument does not prove which entry the helper tested.

    private bool TryClassifyHelperDeletedPredicate(
        IInvocationOperation invocation,
        INamedTypeSymbol owningType,
        out bool isNegated,
        out ISymbol? testedEntry)
    {
        isNegated = false;
        testedEntry = null;
        var target = invocation.TargetMethod.OriginalDefinition;
        foreach (var parameter in target.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
                return false;
        }

        if (target.MethodKind != MethodKind.Ordinary ||
            target.IsVirtual ||
            target.IsOverride ||
            target.IsAbstract ||
            target.ReturnType.SpecialType != SpecialType.System_Boolean ||
            !SymbolEqualityComparer.Default.Equals(
                target.ContainingType.OriginalDefinition,
                owningType.OriginalDefinition))
        {
            return false;
        }

        // A single-return helper returning the Deleted test proves the caller's branch:
        // the branch is taken only when the helper returns true, which implies the test.
        foreach (var reference in target.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not MethodDeclarationSyntax methodSyntax)
                continue;

            var model = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
            var operation = methodSyntax.Body != null
                ? model.GetOperation(methodSyntax.Body) ?? model.GetOperation(methodSyntax)
                : methodSyntax.ExpressionBody != null
                    ? model.GetOperation(methodSyntax.ExpressionBody.Expression)
                        ?? model.GetOperation(methodSyntax)
                    : model.GetOperation(methodSyntax);
            IOperation? returned = null;
            if (operation is IBlockOperation block &&
                block.Operations.Length == 1 &&
                block.Operations[0] is IReturnOperation { ReturnedValue: { } value })
            {
                returned = value;
            }
            else if (operation is not IBlockOperation)
            {
                returned = operation;
            }

            returned = returned?.UnwrapConversions();
            if (returned is IBinaryOperation binary &&
                TryGetDeletedComparisonPolarity(binary, out isNegated, out _) &&
                TryGetTestedParameter(binary, target, out var binaryTested) &&
                TryGetTestedArgumentSymbol(invocation, target, binaryTested, out testedEntry))
            {
                return true;
            }

            if (returned is IIsPatternOperation isPattern &&
                IsStateProperty(isPattern.Value) &&
                TryGetDeletedPatternPolarity(isPattern.Pattern, out isNegated) &&
                TryGetTestedParameter(isPattern.Value, target, out var patternTested) &&
                TryGetTestedArgumentSymbol(invocation, target, patternTested, out testedEntry))
            {
                return true;
            }

            isNegated = false;
            testedEntry = null;
        }

        return false;
    }

    private static bool TryGetTestedParameter(
        IBinaryOperation binary,
        IMethodSymbol target,
        out int testedOrdinal)
    {
        var leftDeleted = IsEntityStateMember(binary.LeftOperand, "Deleted");
        var stateOperand = leftDeleted ? binary.RightOperand : binary.LeftOperand;
        if (!IsStateProperty(stateOperand))
        {
            testedOrdinal = -1;
            return false;
        }

        return TryGetTestedParameter(stateOperand, target, out testedOrdinal);
    }

    private static bool TryGetTestedParameter(
        IOperation? operation,
        IMethodSymbol target,
        out int testedOrdinal)
    {
        // The tested entry must flow through the helper's parameters. Captured state
        // (fields, locals, other entries) cannot be linked to the converted entry.
        var current = operation?.UnwrapConversions();
        while (current is IPropertyReferenceOperation { Instance: { } instance })
            current = instance.UnwrapConversions();

        testedOrdinal = -1;
        if (current is not IParameterReferenceOperation parameter)
            return false;

        for (var ordinal = 0; ordinal < target.Parameters.Length; ordinal++)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter.OriginalDefinition,
                    target.Parameters[ordinal].OriginalDefinition))
            {
                testedOrdinal = ordinal;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTestedArgumentSymbol(
        IInvocationOperation invocation,
        IMethodSymbol target,
        int testedOrdinal,
        out ISymbol? testedEntry)
    {
        // The tested argument must be a plain local or parameter reference. Ambient
        // state (fields, properties, other entries) cannot be linked to the entry
        // converted under dominance, so such predicates stay quiet.
        testedEntry = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null ||
                !SymbolEqualityComparer.Default.Equals(
                    argument.Parameter.OriginalDefinition,
                    target.Parameters[testedOrdinal].OriginalDefinition))
            {
                continue;
            }

            var current = argument.Value?.UnwrapConversions();
            while (current is IPropertyReferenceOperation { Instance: { } instance })
                current = instance.UnwrapConversions();

            if (current is ILocalReferenceOperation local)
                testedEntry = local.Local;
            else if (current is IParameterReferenceOperation parameter)
                testedEntry = parameter.Parameter;

            return testedEntry != null;
        }

        return false;
    }


    private static bool TryGetDeletedComparisonPolarity(
        IBinaryOperation binary,
        out bool isNegated,
        out ISymbol? testedEntry)
    {
        isNegated = false;
        testedEntry = null;
        var leftDeleted = IsEntityStateMember(binary.LeftOperand, "Deleted");
        var rightDeleted = IsEntityStateMember(binary.RightOperand, "Deleted");
        if (leftDeleted == rightDeleted)
            return false;
        if (leftDeleted && !IsStateProperty(binary.RightOperand))
            return false;
        if (rightDeleted && !IsStateProperty(binary.LeftOperand))
            return false;

        var stateOperand = leftDeleted ? binary.RightOperand : binary.LeftOperand;
        if (binary.OperatorKind == BinaryOperatorKind.Equals)
        {
            testedEntry = TryGetEntrySymbol(stateOperand);
            return true;
        }

        if (binary.OperatorKind == BinaryOperatorKind.NotEquals)
        {
            isNegated = true;
            testedEntry = TryGetEntrySymbol(stateOperand);
            return true;
        }

        return false;
    }

    private static bool TryGetDeletedPatternPolarity(IPatternOperation? pattern, out bool isNegated)
    {
        isNegated = false;
        if (pattern is IConstantPatternOperation constant &&
            IsEntityStateMember(constant.Value, "Deleted"))
        {
            return true;
        }

        if (pattern is INegatedPatternOperation negated &&
            TryGetDeletedPatternPolarity(negated.Pattern, out var innerNegated))
        {
            isNegated = !innerNegated;
            return true;
        }

        return false;
    }

    private static bool CaseIncludesDeleted(ISwitchCaseOperation switchCase)
    {
        foreach (var clause in switchCase.Clauses)
        {
            switch (clause)
            {
                case ISingleValueCaseClauseOperation single when IsEntityStateMember(single.Value, "Deleted"):
                    return true;
                case IPatternCaseClauseOperation pattern
                    when TryGetDeletedPatternPolarity(pattern.Pattern, out var negated) && !negated:
                    return true;
            }
        }

        return false;
    }

    private static bool BlockHasGoto(IBlockOperation block)
    {
        foreach (var operation in EnumerateOperations(block))
        {
            if (operation is IBranchOperation branch && branch.BranchKind == BranchKind.GoTo)
                return true;
        }

        return false;
    }

    private static bool IsStateProperty(IOperation? operation)
    {
        var current = operation?.UnwrapConversions();
        if (current is not IPropertyReferenceOperation property || property.Property.Name != "State")
            return false;

        var containingType = property.Property.ContainingType;
        if (containingType?.Name != "EntityEntry")
            return false;

        var ns = containingType.ContainingNamespace?.ToString();
        return ns is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.ChangeTracking";
    }

    private static bool IsUnconditionalExit(IOperation? operation)
    {
        operation = UnwrapExpressionStatement(operation);
        switch (operation)
        {
            case IReturnOperation:
            case IThrowOperation:
            case IBranchOperation branch when branch.BranchKind is BranchKind.Break or BranchKind.Continue:
                return true;
            case IBlockOperation block when block.Operations.Length > 0:
                for (var i = 0; i < block.Operations.Length - 1; i++)
                {
                    if (MayDivert(block.Operations[i]))
                        return false;
                }

                return IsUnconditionalExit(block.Operations[block.Operations.Length - 1]);
            default:
                return false;
        }
    }

    private static IOperation? UnwrapExpressionStatement(IOperation? operation)
    {
        while (operation is IExpressionStatementOperation expression)
            operation = expression.Operation;
        return operation;
    }

    private static bool MayDivert(IOperation operation)
    {
        var current = UnwrapExpressionStatement(operation);
        return current is IConditionalOperation
            or ISwitchOperation
            or ILoopOperation
            or ITryOperation
            or ILocalFunctionOperation
            or IAnonymousFunctionOperation;
    }

    private static void RecordAssignment(IAssignmentOperation assignment, ConversionAccumulator aggregate)
    {
        if (assignment.Target is not IPropertyReferenceOperation property)
            return;

        if (property.Property.Name == "State")
        {
            if (IsEntityStateMember(assignment.Value, "Modified") ||
                IsEntityStateMember(assignment.Value, "Unchanged"))
            {
                aggregate.ConvertsState = true;
            }

            return;
        }

        if (property.Property.Name == "CurrentValue")
        {
            if (TryGetShadowPropertyName(property.Instance, out var shadowName))
                aggregate.RecordProperty(shadowName, IsConstantTrue(assignment.Value));
            return;
        }

        if (property.Property.Name is "Entity" or "Context" or "ChangeTracker")
            return;

        aggregate.RecordProperty(property.Property.Name, IsConstantTrue(assignment.Value));
        if (property.Instance != null &&
            property.Instance.UnwrapConversions() is IConversionOperation conversion &&
            conversion.Type is INamedTypeSymbol convertedType)
        {
            aggregate.NarrowedEntities.Add(convertedType);
        }
    }

    private static bool TryGetShadowPropertyName(IOperation? instance, out string name)
    {
        name = null!;
        var current = instance?.UnwrapConversions();
        if (current is not IInvocationOperation invocation || invocation.TargetMethod.Name != "Property")
            return false;

        if (invocation.Arguments.Length == 0)
            return false;

        var argument = invocation.Arguments[0].Value.UnwrapConversions();
        if (argument.ConstantValue.HasValue && argument.ConstantValue.Value is string constantName &&
            constantName.Length > 0)
        {
            name = constantName;
            return true;
        }

        return false;
    }

    private static bool IsEntityProperty(IOperation? operation)
    {
        var current = operation?.UnwrapConversions();
        return current is IPropertyReferenceOperation property && property.Property.Name == "Entity";
    }

    private static bool IsEntityStateMember(IOperation? operation, string memberName)
    {
        var current = operation?.UnwrapConversions();
        ISymbol? symbol = current switch
        {
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null
        };

        if (symbol == null || symbol.Name != memberName)
            return false;

        return symbol.ContainingType?.Name == "EntityState" &&
               symbol.ContainingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsConstantTrue(IOperation? operation)
    {
        var current = operation?.UnwrapConversions();
        return current?.ConstantValue.HasValue == true && current.ConstantValue.Value is true;
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var descendant in EnumerateOperations(child))
                yield return descendant;
        }
    }

    private static HashSet<ISymbol>? AddDominated(HashSet<ISymbol>? dominated, ISymbol? entry)
    {
        if (entry == null)
            return dominated;

        var next = dominated == null
            ? new HashSet<ISymbol>(SymbolEqualityComparer.Default)
            : new HashSet<ISymbol>(dominated, SymbolEqualityComparer.Default);
        next.Add(entry);
        return next;
    }

    private static HashSet<ISymbol>? MapDominatedArguments(
        IInvocationOperation invocation,
        IMethodSymbol target,
        HashSet<ISymbol>? dominated)
    {
        if (dominated == null)
            return null;

        HashSet<ISymbol>? mapped = null;
        foreach (var entry in dominated)
        {
            // Fields remain visible inside the callee; locals do not.
            if (entry is IFieldSymbol)
                (mapped ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default)).Add(entry);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null)
                continue;

            if (TryGetEntrySymbol(argument.Value) is { } argumentEntry && dominated.Contains(argumentEntry))
                (mapped ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default))
                    .Add(argument.Parameter.OriginalDefinition);
        }

        return mapped;
    }

    private static long DominatedParameterMask(IMethodSymbol method, HashSet<ISymbol>? dominated)
    {
        if (dominated == null)
            return 0;

        long mask = 0;
        for (var ordinal = 0; ordinal < method.Parameters.Length && ordinal < 64; ordinal++)
        {
            if (dominated.Contains(method.Parameters[ordinal].OriginalDefinition))
                mask |= 1L << ordinal;
        }

        return mask;
    }

    private static ISymbol? TryGetEntrySymbol(IOperation? operation)
    {
        var current = operation?.UnwrapConversions();
        while (true)
        {
            switch (current)
            {
                case ILocalReferenceOperation local:
                    return local.Local;
                case IParameterReferenceOperation parameter:
                    return parameter.Parameter;
                case IFieldReferenceOperation field:
                    return field.Field;
                case IPropertyReferenceOperation { Instance: { } instance }:
                    current = instance.UnwrapConversions();
                    continue;
                case IInvocationOperation { Instance: { } receiver }:
                    current = receiver.UnwrapConversions();
                    continue;
                default:
                    return null;
            }
        }
    }

    private readonly struct MethodDominanceKey : IEquatable<MethodDominanceKey>
    {
        private readonly IMethodSymbol method;
        private readonly long dominatedParameterMask;

        public MethodDominanceKey(IMethodSymbol method, long dominatedParameterMask)
        {
            this.method = method;
            this.dominatedParameterMask = dominatedParameterMask;
        }

        public bool Equals(MethodDominanceKey other)
        {
            return dominatedParameterMask == other.dominatedParameterMask &&
                   SymbolEqualityComparer.Default.Equals(method, other.method);
        }

        public override bool Equals(object? obj)
        {
            return obj is MethodDominanceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (SymbolEqualityComparer.Default.GetHashCode(method) * 397) ^
                   dominatedParameterMask.GetHashCode();
        }
    }

    private sealed class MethodDominanceSet
    {
        private readonly HashSet<MethodDominanceKey> visited = new();

        public bool Add(IMethodSymbol method, long dominatedParameterMask)
        {
            return visited.Add(new MethodDominanceKey(method, dominatedParameterMask));
        }
    }

    private sealed class ConversionAccumulator
    {
        public bool ReadsDeleted;
        public bool ConvertsState;
        public bool SawUntypedEntries;
        public HashSet<INamedTypeSymbol> TypedEntities { get; } = new(SymbolEqualityComparer.Default);
        public HashSet<INamedTypeSymbol> NarrowedEntities { get; } = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, bool> properties = new();

        public void RecordProperty(string name, bool isConstantTrue)
        {
            if (properties.TryGetValue(name, out var existing))
                properties[name] = existing && isConstantTrue;
            else
                properties[name] = isConstantTrue;
        }

        public ConversionScan ToScan()
        {
            var entities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var entity in TypedEntities)
                entities.Add(entity);
            foreach (var entity in NarrowedEntities)
                entities.Add(entity);

            if (SawUntypedEntries && entities.Count == 0)
            {
                // Untyped Entries() with no cast/pattern narrowing covers the whole context.
            }
            else if (!SawUntypedEntries && entities.Count == 0 && ReadsDeleted)
            {
                // Deleted was observed without Entries(); treat as context-wide.
            }

            string? singleBoolTrue = null;
            if (properties.Count == 1)
            {
                foreach (var pair in properties)
                {
                    if (pair.Value)
                        singleBoolTrue = pair.Key;
                }
            }

            return new ConversionScan(ReadsDeleted, ConvertsState, entities, singleBoolTrue, properties.Count > 0);
        }
    }
}
