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
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol method)
                continue;

            if (!IsPipelineMethod(method, isInterceptor))
                continue;

            ScanMethodTree(method, type, visited, 0, aggregate, cancellationToken);
        }

        return aggregate.ToScan();
    }

    private void ScanMethodTree(
        IMethodSymbol method,
        INamedTypeSymbol owningType,
        HashSet<IMethodSymbol> visited,
        int depth,
        ConversionAccumulator aggregate,
        CancellationToken cancellationToken)
    {
        if (depth > 4 || !visited.Add(method))
            return;

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                continue;

            var tree = declaration.SyntaxTree;
            var model = compilation.GetSemanticModel(tree);
            var operation = model.GetOperation(declaration, cancellationToken);
            if (operation == null)
                continue;

            WalkConversionOperations(operation, aggregate, cancellationToken);

            foreach (var child in EnumerateOperations(operation))
            {
                if (child is not IInvocationOperation invocation)
                    continue;

                var target = invocation.TargetMethod.OriginalDefinition;
                if (!SymbolEqualityComparer.Default.Equals(target.ContainingType, owningType))
                    continue;

                ScanMethodTree(target, owningType, visited, depth + 1, aggregate, cancellationToken);
            }
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

    private static void WalkConversionOperations(
        IOperation root,
        ConversionAccumulator aggregate,
        CancellationToken cancellationToken)
    {
        foreach (var operation in EnumerateOperations(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsEntityStateMember(operation, "Deleted"))
                aggregate.ReadsDeleted = true;

            if (operation is IInvocationOperation invocation &&
                invocation.TargetMethod.Name == "Entries")
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

            if (operation is IConversionOperation conversion &&
                conversion.Type is INamedTypeSymbol convertedType &&
                IsEntityProperty(conversion.Operand))
            {
                aggregate.NarrowedEntities.Add(convertedType);
            }

            if (operation is IAssignmentOperation assignment)
                RecordAssignment(assignment, aggregate);
        }
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
