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

            ScanMethodTree(method, type, visited, 0, aggregate, deletedDominates: false, cancellationToken);
        }

        return aggregate.ToScan();
    }

    private void ScanMethodTree(
        IMethodSymbol method,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        bool deletedDominates,
        CancellationToken cancellationToken)
    {
        method = method.OriginalDefinition;
        if (depth > 4 || !visited.Add(method, deletedDominates))
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
                deletedDominates,
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
        bool deletedDominates,
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
                        deletedDominates,
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
                    deletedDominates,
                    cancellationToken);
                return;

            case IConditionalOperation conditional:
                WalkConditional(
                    conditional,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    deletedDominates,
                    cancellationToken);
                return;

            case ISwitchOperation switchOperation:
                WalkSwitch(
                    switchOperation,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    deletedDominates,
                    cancellationToken);
                return;
        }

        ObserveOperation(operation, owningType, visited, depth, aggregate, deletedDominates, cancellationToken);

        foreach (var child in operation.ChildOperations)
        {
            WalkConversionOperations(
                child,
                owningType,
                visited,
                depth,
                aggregate,
                deletedDominates,
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
        bool deletedDominates,
        CancellationToken cancellationToken)
    {
        var dominate = deletedDominates;
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
                        cancellationToken) &&
                    allowSequentialDominance)
                {
                    dominate = true;
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

    private bool WalkConditional(
        IConditionalOperation conditional,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        bool deletedDominates,
        CancellationToken cancellationToken)
    {
        WalkConversionOperations(
            conditional.Condition,
            owningType,
            visited,
            depth,
            aggregate,
            deletedDominates,
            isScanRoot: false,
            cancellationToken);

        if (TryClassifyDeletedCondition(conditional.Condition, out var thenDeleted, out var elseDeleted))
        {
            WalkConversionOperations(
                conditional.WhenTrue,
                owningType,
                visited,
                depth,
                aggregate,
                deletedDominates || thenDeleted,
                isScanRoot: false,
                cancellationToken);
            WalkConversionOperations(
                conditional.WhenFalse,
                owningType,
                visited,
                depth,
                aggregate,
                deletedDominates || elseDeleted,
                isScanRoot: false,
                cancellationToken);
            return elseDeleted &&
                   conditional.WhenFalse is null &&
                   IsUnconditionalExit(conditional.WhenTrue);
        }

        WalkConversionOperations(
            conditional.WhenTrue,
            owningType,
            visited,
            depth,
            aggregate,
            deletedDominates,
            isScanRoot: false,
            cancellationToken);
        WalkConversionOperations(
            conditional.WhenFalse,
            owningType,
            visited,
            depth,
            aggregate,
            deletedDominates,
            isScanRoot: false,
            cancellationToken);
        return false;
    }

    private void WalkSwitch(
        ISwitchOperation switchOperation,
        INamedTypeSymbol owningType,
        MethodDominanceSet visited,
        int depth,
        ConversionAccumulator aggregate,
        bool deletedDominates,
        CancellationToken cancellationToken)
    {
        WalkConversionOperations(
            switchOperation.Value,
            owningType,
            visited,
            depth,
            aggregate,
            deletedDominates,
            isScanRoot: false,
            cancellationToken);

        foreach (var switchCase in switchOperation.Cases)
        {
            var caseDominates = deletedDominates ||
                                (IsStateProperty(switchOperation.Value) && CaseIncludesDeleted(switchCase));
            foreach (var clause in switchCase.Clauses)
            {
                WalkConversionOperations(
                    clause,
                    owningType,
                    visited,
                    depth,
                    aggregate,
                    deletedDominates,
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
        bool deletedDominates,
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
                    deletedDominates,
                    cancellationToken);
            }
        }

        if (operation is IConversionOperation conversion &&
            deletedDominates &&
            conversion.Type is INamedTypeSymbol convertedType &&
            IsEntityProperty(conversion.Operand))
        {
            aggregate.NarrowedEntities.Add(convertedType);
        }

        if (operation is IAssignmentOperation assignment && deletedDominates)
            RecordAssignment(assignment, aggregate);
    }

    private static bool TryClassifyDeletedCondition(
        IOperation? condition,
        out bool thenDeleted,
        out bool elseDeleted)
    {
        thenDeleted = false;
        elseDeleted = false;
        condition = condition?.UnwrapConversions();
        if (condition is IBinaryOperation binary &&
            TryGetDeletedComparisonPolarity(binary, out var isNegated))
        {
            if (isNegated)
                elseDeleted = true;
            else
                thenDeleted = true;
            return true;
        }

        if (condition is IIsPatternOperation isPattern &&
            IsStateProperty(isPattern.Value) &&
            TryGetDeletedPatternPolarity(isPattern.Pattern, out var patternNegated))
        {
            if (patternNegated)
                elseDeleted = true;
            else
                thenDeleted = true;
            return true;
        }

        return false;
    }

    private static bool TryGetDeletedComparisonPolarity(IBinaryOperation binary, out bool isNegated)
    {
        isNegated = false;
        var leftDeleted = IsEntityStateMember(binary.LeftOperand, "Deleted");
        var rightDeleted = IsEntityStateMember(binary.RightOperand, "Deleted");
        if (leftDeleted == rightDeleted)
            return false;
        if (leftDeleted && !IsStateProperty(binary.RightOperand))
            return false;
        if (rightDeleted && !IsStateProperty(binary.LeftOperand))
            return false;

        if (binary.OperatorKind == BinaryOperatorKind.Equals)
            return true;

        if (binary.OperatorKind == BinaryOperatorKind.NotEquals)
        {
            isNegated = true;
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

    private sealed class MethodDominanceSet
    {
        private readonly HashSet<IMethodSymbol> dominated = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> undominated = new(SymbolEqualityComparer.Default);

        public bool Add(IMethodSymbol method, bool deletedDominates)
        {
            return (deletedDominates ? dominated : undominated).Add(method);
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
