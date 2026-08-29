using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC048_LostUpdateRisk;

internal sealed class LostUpdateCompilationEvidence
{
    private readonly Compilation _compilation;
    private readonly object _fluentScanGate = new();
    private readonly ConcurrentDictionary<IMethodSymbol, HelperSummary> _helperSummaries = new(
        SymbolEqualityComparer.Default
    );
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
    >? _fluentConcurrencyModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
    >? _fluentStoreGeneratedModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
    >? _fluentRowVersionModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
    >? _fluentNamedConcurrencyModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
    >? _fluentNamedStoreGeneratedModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, bool>
    >? _fluentKeylessModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
    >? _fluentPrimaryKeyModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
    >? _fluentAlternateKeyModels;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
    >? _fluentIgnoredProperties;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
    >? _fluentDefinitelyIgnoredProperties;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>>
    >? _fluentIgnoredPropertyNames;
    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
    >? _fluentMappedProperties;
    private ImmutableDictionary<INamedTypeSymbol, ContextTrackingModel>? _contextTrackingModels;

    private LostUpdateCompilationEvidence(Compilation compilation)
    {
        _compilation = compilation;
    }

    internal static LostUpdateCompilationEvidence Create(
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new LostUpdateCompilationEvidence(compilation);
    }

    internal bool HasConcurrencyProtection(
        INamedTypeSymbol entityType,
        IPropertySymbol mutationProperty,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        var timestampProperties = ImmutableArray.CreateBuilder<IPropertySymbol>();
        var concurrencyProperties = ImmutableArray.CreateBuilder<IPropertySymbol>();
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer)
                    continue;

                foreach (var attribute in property.GetAttributes())
                {
                    switch (attribute.AttributeClass?.ToDisplayString())
                    {
                        case "System.ComponentModel.DataAnnotations.TimestampAttribute":
                            timestampProperties.Add(property.OriginalDefinition);
                            break;
                        case "System.ComponentModel.DataAnnotations.ConcurrencyCheckAttribute":
                            concurrencyProperties.Add(property.OriginalDefinition);
                            break;
                    }
                }
            }
        }

        var effectiveStates = new Dictionary<IPropertySymbol, bool>(SymbolEqualityComparer.Default);
        INamedTypeSymbol? contextType = null;
        if (TryGetContextType(contextSymbol, out var resolvedContextType))
        {
            contextType = resolvedContextType;
            var fluentConcurrencyModels = GetFluentConcurrencyModels(cancellationToken);
            foreach (
                var propertyState in GetEffectivePropertyStates(
                    fluentConcurrencyModels,
                    contextType,
                    entityType
                )
            )
            {
                effectiveStates[propertyState.Key] = propertyState.Value;
            }
        }

        var originalMutationProperty = mutationProperty.OriginalDefinition;
        if (
            effectiveStates.TryGetValue(originalMutationProperty, out var mutationEnabled)
            && mutationEnabled
            && !IsDefinitelyIgnoredPropertyForConcurrency(
                entityType,
                originalMutationProperty,
                contextSymbol,
                cancellationToken
            )
        )
        {
            return true;
        }

        if (
            contextType != null
            && (
                HasFluentRowVersionProtection(
                    contextType,
                    entityType,
                    contextSymbol,
                    cancellationToken
                ) || HasNamedConcurrencyProtection(contextType, entityType, cancellationToken)
            )
        )
        {
            return true;
        }

        foreach (var timestampProperty in timestampProperties)
        {
            if (
                !IsDefinitelyIgnoredPropertyForConcurrency(
                    entityType,
                    timestampProperty,
                    contextSymbol,
                    cancellationToken
                )
                && (
                    !effectiveStates.TryGetValue(timestampProperty, out var timestampEnabled)
                    || timestampEnabled
                )
            )
            {
                return true;
            }
        }

        if (
            concurrencyProperties.Any(property =>
                SymbolEqualityComparer.Default.Equals(property, originalMutationProperty)
            )
            && !IsDefinitelyIgnoredPropertyForConcurrency(
                entityType,
                originalMutationProperty,
                contextSymbol,
                cancellationToken
            )
            && (
                !effectiveStates.TryGetValue(
                    originalMutationProperty,
                    out var mutationAttributeEnabled
                ) || mutationAttributeEnabled
            )
        )
        {
            return true;
        }

        return false;
    }

    internal bool IsKeylessEntity(
        INamedTypeSymbol entityType,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        var attributed = false;
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            attributed |= current
                .GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString()
                    == "Microsoft.EntityFrameworkCore.KeylessAttribute"
                );
        }

        GetFluentConcurrencyModels(cancellationToken);
        if (!TryGetContextType(contextSymbol, out var contextType) || _fluentKeylessModels == null)
        {
            return attributed;
        }

        ImmutableDictionary<INamedTypeSymbol, bool>? entityStates = null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (_fluentKeylessModels.TryGetValue(current.OriginalDefinition, out entityStates))
            {
                break;
            }
        }

        bool? effective = null;
        if (entityStates != null)
        {
            var hierarchy = new Stack<INamedTypeSymbol>();
            for (
                INamedTypeSymbol? current = entityType;
                current != null;
                current = current.BaseType
            )
            {
                hierarchy.Push(current.OriginalDefinition);
            }

            while (hierarchy.Count > 0)
            {
                if (entityStates.TryGetValue(hierarchy.Pop(), out var keyless))
                    effective = keyless;
            }
        }

        return effective ?? attributed;
    }

    internal bool TryGetFluentPrimaryKeys(
        INamedTypeSymbol entityType,
        ISymbol contextSymbol,
        CancellationToken cancellationToken,
        out ImmutableHashSet<IPropertySymbol> primaryKeys
    )
    {
        GetFluentConcurrencyModels(cancellationToken);
        if (
            !TryGetContextType(contextSymbol, out var contextType)
            || _fluentPrimaryKeyModels == null
        )
        {
            primaryKeys = null!;
            return false;
        }

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>? entityStates =
            null;
        for (var current = contextType; current != null; current = current.BaseType)
        {
            if (_fluentPrimaryKeyModels.TryGetValue(current.OriginalDefinition, out entityStates))
            {
                break;
            }
        }

        ImmutableHashSet<IPropertySymbol>? effective = null;
        if (entityStates != null)
        {
            var hierarchy = new Stack<INamedTypeSymbol>();
            for (var current = entityType; current != null; current = current.BaseType)
                hierarchy.Push(current.OriginalDefinition);

            while (hierarchy.Count > 0)
            {
                if (entityStates.TryGetValue(hierarchy.Pop(), out var configuredKeys))
                    effective = configuredKeys;
            }
        }

        if (effective == null)
        {
            primaryKeys = null!;
            return false;
        }

        primaryKeys = effective;
        return true;
    }

    internal bool TryGetFluentAlternateKeys(
        INamedTypeSymbol entityType,
        ISymbol contextSymbol,
        CancellationToken cancellationToken,
        out ImmutableHashSet<IPropertySymbol> alternateKeys
    )
    {
        GetFluentConcurrencyModels(cancellationToken);
        if (
            !TryGetContextType(contextSymbol, out var contextType)
            || _fluentAlternateKeyModels == null
        )
        {
            alternateKeys = null!;
            return false;
        }

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>? entityStates =
            null;
        for (var current = contextType; current != null; current = current.BaseType)
        {
            if (_fluentAlternateKeyModels.TryGetValue(current.OriginalDefinition, out entityStates))
                break;
        }

        if (entityStates == null)
        {
            alternateKeys = null!;
            return false;
        }

        var effective = ImmutableHashSet.CreateBuilder<IPropertySymbol>(
            SymbolEqualityComparer.Default
        );
        var found = false;
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = entityType; current != null; current = current.BaseType)
            hierarchy.Push(current.OriginalDefinition);

        while (hierarchy.Count > 0)
        {
            if (!entityStates.TryGetValue(hierarchy.Pop(), out var configuredKeys))
                continue;

            effective.UnionWith(configuredKeys);
            found = true;
        }

        if (!found)
        {
            alternateKeys = null!;
            return false;
        }

        alternateKeys = effective.ToImmutable();
        return true;
    }

    internal bool IsStoreGeneratedProperty(
        INamedTypeSymbol entityType,
        IPropertySymbol property,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        GetFluentConcurrencyModels(cancellationToken);
        return TryGetContextType(contextSymbol, out var contextType)
            && _fluentStoreGeneratedModels != null
            && GetEffectivePropertyStates(_fluentStoreGeneratedModels, contextType, entityType)
                .TryGetValue(property.OriginalDefinition, out var storeGenerated)
            && storeGenerated;
    }

    internal bool IsIgnoredProperty(
        INamedTypeSymbol entityType,
        IPropertySymbol property,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        var attributedNotMapped = property
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.ToDisplayString()
                == "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute"
            );
        GetFluentConcurrencyModels(cancellationToken);
        if (
            !TryGetContextType(contextSymbol, out var contextType)
            || _fluentIgnoredProperties == null
        )
        {
            return attributedNotMapped;
        }

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>? entityModels =
            null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (_fluentIgnoredProperties.TryGetValue(current.OriginalDefinition, out entityModels))
            {
                break;
            }
        }

        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            if (
                entityModels != null
                && entityModels.TryGetValue(current.OriginalDefinition, out var ignoredProperties)
                && ignoredProperties.Contains(property.OriginalDefinition)
            )
            {
                return true;
            }
        }

        if (IsIgnoredPropertyName(contextType, entityType, property.Name))
            return true;

        return attributedNotMapped
            && !IsExplicitlyMappedProperty(contextType, entityType, property.OriginalDefinition);
    }

    private bool IsDefinitelyIgnoredPropertyForConcurrency(
        INamedTypeSymbol entityType,
        IPropertySymbol property,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        if (IsIgnoredProperty(entityType, property, contextSymbol, cancellationToken))
            return true;

        if (
            !TryGetContextType(contextSymbol, out var contextType)
            || _fluentDefinitelyIgnoredProperties == null
        )
        {
            return false;
        }

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>? entityModels =
            null;
        for (var current = contextType; current != null; current = current.BaseType)
        {
            if (
                _fluentDefinitelyIgnoredProperties.TryGetValue(
                    current.OriginalDefinition,
                    out entityModels
                )
            )
            {
                break;
            }
        }

        for (var current = entityType; current != null; current = current.BaseType)
        {
            if (
                entityModels != null
                && entityModels.TryGetValue(current.OriginalDefinition, out var ignoredProperties)
                && ignoredProperties.Contains(property.OriginalDefinition)
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool IsIgnoredPropertyName(
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType,
        string propertyName
    )
    {
        if (_fluentIgnoredPropertyNames == null)
            return false;

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>>? entityModels = null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (
                _fluentIgnoredPropertyNames.TryGetValue(
                    current.OriginalDefinition,
                    out entityModels
                )
            )
            {
                break;
            }
        }

        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            if (
                entityModels != null
                && entityModels.TryGetValue(current.OriginalDefinition, out var ignoredNames)
                && ignoredNames.Contains(propertyName)
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool IsExplicitlyMappedProperty(
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType,
        IPropertySymbol property
    )
    {
        if (_fluentMappedProperties == null)
            return false;

        ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>? entityModels =
            null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (_fluentMappedProperties.TryGetValue(current.OriginalDefinition, out entityModels))
            {
                break;
            }
        }

        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            if (
                entityModels != null
                && entityModels.TryGetValue(current.OriginalDefinition, out var mappedProperties)
                && mappedProperties.Contains(property)
            )
            {
                return true;
            }
        }

        return false;
    }

    internal bool IsDefaultNoTracking(ISymbol contextSymbol, CancellationToken cancellationToken)
    {
        return TryGetContextTrackingModel(contextSymbol, cancellationToken, out var model)
            && model.DefaultNoTracking;
    }

    internal bool HasIndependentChangeDetection(
        ISymbol contextSymbol,
        INamedTypeSymbol entityType,
        CancellationToken cancellationToken
    )
    {
        return TryGetContextTrackingModel(contextSymbol, cancellationToken, out var model)
            && (
                model.IndependentChangeDetection
                || HasProvenNotificationStrategy(model.NotificationStrategies, entityType)
            );
    }

    private bool TryGetContextTrackingModel(
        ISymbol contextSymbol,
        CancellationToken cancellationToken,
        out ContextTrackingModel model
    )
    {
        var models = GetContextTrackingModels(cancellationToken);
        if (
            TryGetContextType(contextSymbol, out var contextType)
            && models.TryGetValue(contextType.OriginalDefinition, out model)
        )
        {
            return true;
        }

        model = default;
        return false;
    }

    internal bool TryGetHelperSummary(
        IInvocationOperation invocation,
        SyntaxTree callerTree,
        out HelperSummary summary
    )
    {
        var original = invocation.TargetMethod.OriginalDefinition;
        var isLocalFunction = original.MethodKind == MethodKind.LocalFunction;
        if (
            (!isLocalFunction && original.DeclaredAccessibility != Accessibility.Private)
            || !isLocalFunction && original.IsStatic && original.Parameters.Length == 0
            || original.IsAsync && (original.ReturnsVoid || !IsCompletionObserved(invocation))
            || !original.Locations.Any(location =>
                location.IsInSource && location.SourceTree == callerTree
            )
        )
        {
            summary = null!;
            return false;
        }

        if (isLocalFunction)
            return TryGetLocalFunctionSummary(invocation, original, callerTree, out summary);

        if (_helperSummaries.TryGetValue(original, out summary!))
            return true;

        foreach (var syntaxReference in original.DeclaringSyntaxReferences)
        {
            if (
                syntaxReference.SyntaxTree != callerTree
                || syntaxReference.GetSyntax() is not MethodDeclarationSyntax methodSyntax
            )
            {
                continue;
            }

            if (methodSyntax.DescendantNodes().OfType<YieldStatementSyntax>().Any())
                break;

            var model = _compilation.GetSemanticModel(callerTree);
            var bodyOperation =
                model.GetOperation(methodSyntax)
                ?? (
                    methodSyntax.Body != null ? model.GetOperation(methodSyntax.Body)
                    : methodSyntax.ExpressionBody != null
                        ? model.GetOperation(methodSyntax.ExpressionBody.Expression)
                    : null
                );
            if (bodyOperation == null)
                break;

            var created = HelperSummary.Create(original, bodyOperation);
            if (created.IsEmpty)
                break;

            _helperSummaries.TryAdd(original, created);
            summary = created;
            return true;
        }

        summary = null!;
        return false;
    }

    private bool TryGetLocalFunctionSummary(
        IInvocationOperation invocation,
        IMethodSymbol method,
        SyntaxTree callerTree,
        out HelperSummary summary
    )
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            if (
                syntaxReference.SyntaxTree != callerTree
                || syntaxReference.GetSyntax() is not LocalFunctionStatementSyntax syntax
                || syntax.DescendantNodes().OfType<YieldStatementSyntax>().Any()
            )
            {
                continue;
            }

            var model = _compilation.GetSemanticModel(callerTree);
            if (model.GetOperation(syntax) is not ILocalFunctionOperation localFunction)
                break;

            var callerRoot = FindExecutableRoot(invocation);
            var declarationRoot = FindExecutableRoot(localFunction.Parent);
            if (
                callerRoot == null
                || declarationRoot == null
                || !ReferenceEquals(callerRoot, declarationRoot)
                || localFunction.Body == null
                || ContainsInvocationOf(localFunction.Body, method)
                || ContainsEscapedReference(callerRoot, localFunction, method)
            )
            {
                break;
            }

            ControlFlowGraph? flowGraph;
            try
            {
                flowGraph =
                    ControlFlowGraph.Create(localFunction.Body)
                    ?? ControlFlowGraph.Create(syntax, model);
            }
            catch (ArgumentException)
            {
                flowGraph = null;
            }
            catch (InvalidOperationException)
            {
                flowGraph = null;
            }

            var created = HelperSummary.Create(method, localFunction.Body, flowGraph);
            if (created.IsEmpty)
                break;

            _helperSummaries.TryAdd(method, created);
            summary = created;
            return true;
        }

        summary = null!;
        return false;
    }

    private static IOperation? FindExecutableRoot(IOperation? operation)
    {
        for (var current = operation; current != null; current = current.Parent)
        {
            if (
                current
                is IMethodBodyBaseOperation
                    or IAnonymousFunctionOperation
                    or ILocalFunctionOperation
            )
            {
                return current;
            }
        }

        return null;
    }

    private static bool ContainsInvocationOf(IOperation operation, IMethodSymbol method)
    {
        if (
            operation is IInvocationOperation invocation
            && SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.OriginalDefinition,
                method
            )
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsInvocationOf(child, method))
                return true;
        }

        return false;
    }

    private static bool ContainsEscapedReference(
        IOperation operation,
        ILocalFunctionOperation declaration,
        IMethodSymbol method
    )
    {
        if (ReferenceEquals(operation, declaration))
            return false;

        if (
            operation is IMethodReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Method.OriginalDefinition, method)
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsEscapedReference(child, declaration, method))
                return true;
        }

        return false;
    }

    internal bool PrivateHelperPreservesTransaction(
        IInvocationOperation invocation,
        int parameterOrdinal,
        SyntaxTree callerTree
    )
    {
        var method = invocation.TargetMethod.OriginalDefinition;
        if (
            method.DeclaredAccessibility != Accessibility.Private
            || parameterOrdinal < 0
            || parameterOrdinal >= method.Parameters.Length
            || method.IsAsync && (method.ReturnsVoid || !IsCompletionObserved(invocation))
            || !method.Locations.Any(location =>
                location.IsInSource && location.SourceTree == callerTree
            )
        )
        {
            return false;
        }

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            if (
                syntaxReference.SyntaxTree != callerTree
                || syntaxReference.GetSyntax() is not MethodDeclarationSyntax methodSyntax
            )
            {
                continue;
            }

            var model = _compilation.GetSemanticModel(callerTree);
            var body =
                model.GetOperation(methodSyntax)
                ?? (
                    methodSyntax.Body != null ? model.GetOperation(methodSyntax.Body)
                    : methodSyntax.ExpressionBody != null
                        ? model.GetOperation(methodSyntax.ExpressionBody.Expression)
                    : null
                );
            return body != null
                && TransactionParameterRemainsActive(body, method.Parameters[parameterOrdinal]);
        }

        return false;
    }

    private static bool TransactionParameterRemainsActive(
        IOperation operation,
        IParameterSymbol parameter
    )
    {
        if (
            operation is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)
            && !IsNonEscapingTransactionParameterUse(reference)
        )
        {
            return false;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (!TransactionParameterRemainsActive(child, parameter))
                return false;
        }

        return true;
    }

    private static bool IsNonEscapingTransactionParameterUse(IParameterReferenceOperation reference)
    {
        IOperation operation = reference;
        while (
            operation.Parent is IConversionOperation conversion
                && ReferenceEquals(conversion.Operand, operation)
            || operation.Parent is IParenthesizedOperation parenthesized
                && ReferenceEquals(parenthesized.Operand, operation)
        )
        {
            operation = operation.Parent;
        }

        return operation.Parent is IBinaryOperation or IIsPatternOperation or IIsTypeOperation
            || operation.Parent
                is ISimpleAssignmentOperation
                {
                    Target: IDiscardOperation,
                    Value: { } value,
                } assignment
                && ReferenceEquals(value, operation)
                && ReferenceEquals(assignment.Value, operation);
    }

    internal static bool IsCompletionObserved(IInvocationOperation invocation)
    {
        IOperation operation = invocation;
        IOperation awaitedOperation = invocation;
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
            operation.Parent is IInvocationOperation configuredAwait
            && LostUpdateOperationFacts.IsFrameworkConfigureAwait(configuredAwait.TargetMethod)
            && configuredAwait.Instance is { } configuredOperation
            && ReferenceEquals(LostUpdateOperationFacts.Unwrap(configuredOperation), invocation)
        )
        {
            operation = configuredAwait;
            awaitedOperation = configuredAwait;
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

        return operation.Parent is IAwaitOperation awaitOperation
            && ReferenceEquals(
                LostUpdateOperationFacts.Unwrap(awaitOperation.Operation),
                awaitedOperation
            );
    }

    private ImmutableDictionary<INamedTypeSymbol, ContextTrackingModel> GetContextTrackingModels(
        CancellationToken cancellationToken
    )
    {
        lock (_fluentScanGate)
        {
            if (_contextTrackingModels != null)
                return _contextTrackingModels;

            var events = new Dictionary<INamedTypeSymbol, List<ContextTrackingEvent>>(
                SymbolEqualityComparer.Default
            );
            var contextTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var configuringOverrides = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            var modelOverrides = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var tree in _compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = tree.GetRoot(cancellationToken);
                var semanticModel = _compilation.GetSemanticModel(tree);
                foreach (var typeSyntax in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (
                        semanticModel.GetDeclaredSymbol(typeSyntax, cancellationToken)
                            is INamedTypeSymbol contextType
                        && LostUpdateOperationFacts.IsDbContextType(contextType)
                    )
                    {
                        contextTypes.Add(contextType.OriginalDefinition);
                    }
                }

                foreach (
                    var methodSyntax in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                )
                {
                    if (
                        semanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken)
                        is not IMethodSymbol method
                    )
                    {
                        continue;
                    }

                    var contextType = method.ContainingType.OriginalDefinition;
                    if (IsOnConfiguring(method))
                    {
                        configuringOverrides.Add(contextType);
                        GetOrAddTrackingEvents(events, contextType);
                    }
                    else if (IsOnModelCreating(method))
                    {
                        modelOverrides.Add(contextType);
                        GetOrAddTrackingEvents(events, contextType);
                    }
                }

                foreach (
                    var invocationSyntax in root.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .OrderBy(invocation => invocation.SpanStart)
                )
                {
                    if (
                        semanticModel.GetOperation(invocationSyntax, cancellationToken)
                        is not IInvocationOperation invocation
                    )
                    {
                        continue;
                    }

                    if (
                        TryGetBaseContextTrackingCall(
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken,
                            out var derivedContext,
                            out var baseContext,
                            out var hook
                        )
                    )
                    {
                        GetOrAddTrackingEvents(events, derivedContext)
                            .Add(
                                ContextTrackingEvent.ForBaseCall(
                                    invocationSyntax.SpanStart,
                                    baseContext,
                                    hook,
                                    IsConditionallyExecuted(
                                        invocationSyntax,
                                        semanticModel,
                                        cancellationToken
                                    )
                                        || HasUnsupportedNestedExecutable(
                                            invocationSyntax,
                                            semanticModel,
                                            cancellationToken
                                        )
                                )
                            );
                        continue;
                    }

                    if (
                        TryGetOnConfiguringContext(
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken,
                            out var configuredContext
                        )
                    )
                    {
                        AddOptionsTrackingEvent(
                            events,
                            configuredContext,
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            isRegistration: false,
                            cancellationToken
                        );
                    }
                    else if (
                        TryGetRegisteredContext(
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken,
                            out configuredContext
                        )
                    )
                    {
                        AddOptionsTrackingEvent(
                            events,
                            configuredContext,
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            isRegistration: true,
                            cancellationToken
                        );
                    }

                    if (
                        TryGetDirectModelContext(
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken,
                            out var modelContext
                        )
                    )
                    {
                        AddModelTrackingEvent(
                            events,
                            modelContext,
                            entityType: null,
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken
                        );
                    }
                    else if (
                        TryGetEntityModelContext(
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken,
                            out modelContext,
                            out var modelEntityType
                        )
                    )
                    {
                        AddModelTrackingEvent(
                            events,
                            modelContext,
                            modelEntityType,
                            invocation,
                            invocationSyntax,
                            semanticModel,
                            cancellationToken
                        );
                    }
                }
            }

            var builder = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, ContextTrackingModel>(
                SymbolEqualityComparer.Default
            );
            var configuringCache = new Dictionary<INamedTypeSymbol, ContextTrackingState>(
                SymbolEqualityComparer.Default
            );
            var modelCache = new Dictionary<INamedTypeSymbol, ContextTrackingState>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in contextTypes)
            {
                var configuring = BuildEffectiveContextTrackingState(
                    contextType,
                    ContextTrackingHook.Configuring,
                    events,
                    configuringOverrides,
                    configuringCache,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                );
                var model = BuildEffectiveContextTrackingState(
                    contextType,
                    ContextTrackingHook.ModelCreating,
                    events,
                    modelOverrides,
                    modelCache,
                    new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                );
                var registrations = events.TryGetValue(contextType, out var contextEvents)
                    ? contextEvents.Where(item => item.IsRegistration).ToArray()
                    : Array.Empty<ContextTrackingEvent>();

                var notificationStrategies = model.NotificationStrategies.IsDefault
                    ? ImmutableArray<NotificationTrackingEvent>.Empty
                    : model.NotificationStrategies;
                builder[contextType] = new ContextTrackingModel(
                    IsProvenEnabled(
                        configuring.HasQueryEvent,
                        configuring.DefaultNoTracking,
                        registrations
                            .Where(item => item.IsQueryEvent)
                            .Select(item => item.DefaultNoTracking)
                    ),
                    IsProvenEnabled(
                        configuring.HasProxyEvent,
                        configuring.ChangeTrackingProxies,
                        registrations
                            .Where(item => item.IsProxyEvent)
                            .Select(item => item.ChangeTrackingProxies)
                    ),
                    notificationStrategies
                );
            }

            _contextTrackingModels = builder.ToImmutable();
            return _contextTrackingModels;
        }
    }

    private static ContextTrackingState BuildEffectiveContextTrackingState(
        INamedTypeSymbol contextType,
        ContextTrackingHook hook,
        Dictionary<INamedTypeSymbol, List<ContextTrackingEvent>> events,
        HashSet<INamedTypeSymbol> overrides,
        Dictionary<INamedTypeSymbol, ContextTrackingState> cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        contextType = contextType.OriginalDefinition;
        if (cache.TryGetValue(contextType, out var cached))
            return cached;
        if (!building.Add(contextType))
            return default;

        if (!overrides.Contains(contextType))
        {
            var baseType = contextType.BaseType;
            var inherited =
                baseType != null && LostUpdateOperationFacts.IsDbContextType(baseType)
                    ? BuildEffectiveContextTrackingState(
                        baseType.OriginalDefinition,
                        hook,
                        events,
                        overrides,
                        cache,
                        building
                    )
                    : default;
            building.Remove(contextType);
            cache[contextType] = inherited;
            return inherited;
        }

        var hasQueryEvent = false;
        bool? defaultNoTracking = null;
        var hasProxyEvent = false;
        bool? changeTrackingProxies = null;
        var notificationStrategies = ImmutableArray.CreateBuilder<NotificationTrackingEvent>();
        if (events.TryGetValue(contextType, out var contextEvents))
        {
            foreach (
                var trackingEvent in contextEvents
                    .Where(item => !item.IsRegistration && item.Hook == hook)
                    .OrderBy(item => item.Position)
            )
            {
                if (trackingEvent.BaseContextType != null)
                {
                    var baseState = BuildEffectiveContextTrackingState(
                        trackingEvent.BaseContextType,
                        hook,
                        events,
                        overrides,
                        cache,
                        building
                    );
                    ApplyInheritedTrackingValue(
                        baseState.HasQueryEvent,
                        baseState.DefaultNoTracking,
                        trackingEvent.IsConditionalBaseCall,
                        ref hasQueryEvent,
                        ref defaultNoTracking
                    );
                    ApplyInheritedTrackingValue(
                        baseState.HasProxyEvent,
                        baseState.ChangeTrackingProxies,
                        trackingEvent.IsConditionalBaseCall,
                        ref hasProxyEvent,
                        ref changeTrackingProxies
                    );
                    var inheritedStrategies = baseState.NotificationStrategies.IsDefault
                        ? ImmutableArray<NotificationTrackingEvent>.Empty
                        : baseState.NotificationStrategies;
                    foreach (var inheritedStrategy in inheritedStrategies)
                    {
                        notificationStrategies.Add(
                            trackingEvent.IsConditionalBaseCall
                                ? inheritedStrategy.WithAmbiguousValue()
                                : inheritedStrategy
                        );
                    }
                    continue;
                }

                if (trackingEvent.IsQueryEvent)
                {
                    hasQueryEvent = true;
                    defaultNoTracking = trackingEvent.DefaultNoTracking;
                }

                if (trackingEvent.IsProxyEvent)
                {
                    hasProxyEvent = true;
                    changeTrackingProxies = trackingEvent.ChangeTrackingProxies;
                }

                if (trackingEvent.IsModelEvent)
                {
                    notificationStrategies.Add(
                        new NotificationTrackingEvent(
                            trackingEvent.EntityType,
                            trackingEvent.NotificationStrategy
                        )
                    );
                }
            }
        }

        building.Remove(contextType);
        var result = new ContextTrackingState(
            hasQueryEvent,
            defaultNoTracking,
            hasProxyEvent,
            changeTrackingProxies,
            notificationStrategies.ToImmutable()
        );
        cache[contextType] = result;
        return result;
    }

    private static void ApplyInheritedTrackingValue(
        bool inheritedHasValue,
        bool? inheritedValue,
        bool isConditional,
        ref bool hasValue,
        ref bool? value
    )
    {
        if (!inheritedHasValue)
            return;

        if (isConditional && (!hasValue || !Nullable.Equals(value, inheritedValue)))
        {
            hasValue = true;
            value = null;
            return;
        }

        hasValue = true;
        value = inheritedValue;
    }

    private static bool IsProvenEnabled(
        bool hasHookEvent,
        bool? hookValue,
        IEnumerable<bool?> additionalValues
    )
    {
        var hasEvidence = hasHookEvent;
        var proven = !hasHookEvent || hookValue == true;
        foreach (var value in additionalValues)
        {
            hasEvidence = true;
            proven &= value == true;
        }

        return hasEvidence && proven;
    }

    private static bool HasProvenNotificationStrategy(
        ImmutableArray<NotificationTrackingEvent> strategies,
        INamedTypeSymbol entityType
    )
    {
        var hasApplicableStrategy = false;
        bool? effectiveStrategy = null;
        foreach (var strategy in strategies)
        {
            if (
                strategy.EntityType != null
                && !IsSameOrBaseEntityType(strategy.EntityType, entityType)
            )
            {
                continue;
            }

            hasApplicableStrategy = true;
            effectiveStrategy = strategy.Value;
        }

        return hasApplicableStrategy && effectiveStrategy == true;
    }

    private static bool IsSameOrBaseEntityType(
        INamedTypeSymbol configuredType,
        INamedTypeSymbol entityType
    )
    {
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    configuredType.OriginalDefinition,
                    current.OriginalDefinition
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static void AddOptionsTrackingEvent(
        Dictionary<INamedTypeSymbol, List<ContextTrackingEvent>> events,
        INamedTypeSymbol contextType,
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        bool isRegistration,
        CancellationToken cancellationToken
    )
    {
        bool? noTracking = null;
        bool? proxies = null;
        var isQueryEvent = false;
        if (IsRealEfOptionsMethod(invocation.TargetMethod, "UseQueryTrackingBehavior"))
        {
            isQueryEvent = true;
            noTracking = TryGetEnumArgument(
                invocation,
                "Microsoft.EntityFrameworkCore.QueryTrackingBehavior",
                out var behavior
            )
                ? behavior is "NoTracking" or "NoTrackingWithIdentityResolution"
                : null;
        }
        else if (IsRealEfOptionsMethod(invocation.TargetMethod, "UseChangeTrackingProxies"))
        {
            proxies = TryGetOptionalBooleanArgument(invocation, out var enabled) ? enabled : null;
        }
        else
        {
            return;
        }

        if (
            IsConditionallyExecuted(syntax, model, cancellationToken)
            || !isRegistration && HasUnsupportedNestedExecutable(syntax, model, cancellationToken)
        )
        {
            noTracking = null;
            proxies = null;
        }

        GetOrAddTrackingEvents(events, contextType)
            .Add(
                ContextTrackingEvent.ForOptions(
                    syntax.SpanStart,
                    noTracking,
                    proxies,
                    isQueryEvent,
                    isRegistration
                )
            );
    }

    private static void AddModelTrackingEvent(
        Dictionary<INamedTypeSymbol, List<ContextTrackingEvent>> events,
        INamedTypeSymbol contextType,
        INamedTypeSymbol? entityType,
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        if (
            entityType == null
                ? !IsRealEfModelMethod(invocation.TargetMethod, "HasChangeTrackingStrategy")
                : !IsRealEfEntityTrackingMethod(invocation.TargetMethod)
        )
        {
            return;
        }

        bool? notificationStrategy = TryGetEnumArgument(
            invocation,
            "Microsoft.EntityFrameworkCore.ChangeTrackingStrategy",
            out var strategy
        )
            ? strategy
                is "ChangedNotifications"
                    or "ChangingAndChangedNotifications"
                    or "ChangingAndChangedNotificationsWithOriginalValues"
            : null;
        if (
            IsConditionallyExecuted(syntax, model, cancellationToken)
            || HasUnsupportedNestedExecutable(syntax, model, cancellationToken)
        )
        {
            notificationStrategy = null;
        }

        GetOrAddTrackingEvents(events, contextType)
            .Add(
                ContextTrackingEvent.ForModel(
                    syntax.SpanStart,
                    entityType?.OriginalDefinition,
                    notificationStrategy
                )
            );
    }

    private static List<ContextTrackingEvent> GetOrAddTrackingEvents(
        Dictionary<INamedTypeSymbol, List<ContextTrackingEvent>> events,
        INamedTypeSymbol contextType
    )
    {
        contextType = contextType.OriginalDefinition;
        if (!events.TryGetValue(contextType, out var result))
        {
            result = new List<ContextTrackingEvent>();
            events.Add(contextType, result);
        }

        return result;
    }

    private static bool TryGetBaseContextTrackingCall(
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol contextType,
        out INamedTypeSymbol baseContextType,
        out ContextTrackingHook hook
    )
    {
        var methodSyntax = syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (
            methodSyntax != null
            && model.GetDeclaredSymbol(methodSyntax, cancellationToken) is IMethodSymbol method
            && syntax.Expression
                is MemberAccessExpressionSyntax
                {
                    Expression: BaseExpressionSyntax,
                    Name.Identifier.ValueText: var methodName,
                }
            && invocation.TargetMethod.Name == methodName
            && invocation.TargetMethod.Parameters.Length == 1
            && invocation.Arguments.Length == 1
            && ReferencesParameter(invocation.Arguments[0].Value, method.Parameters[0])
            && LostUpdateOperationFacts.IsDbContextType(invocation.TargetMethod.ContainingType)
            && (
                IsOnConfiguring(method) && methodName == "OnConfiguring"
                || IsOnModelCreating(method) && methodName == "OnModelCreating"
            )
        )
        {
            contextType = method.ContainingType.OriginalDefinition;
            baseContextType = invocation.TargetMethod.ContainingType.OriginalDefinition;
            hook =
                methodName == "OnConfiguring"
                    ? ContextTrackingHook.Configuring
                    : ContextTrackingHook.ModelCreating;
            return true;
        }

        contextType = null!;
        baseContextType = null!;
        hook = default;
        return false;
    }

    private static bool TryGetOnConfiguringContext(
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol contextType
    )
    {
        var methodSyntax = syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (
            methodSyntax != null
            && model.GetDeclaredSymbol(methodSyntax, cancellationToken) is IMethodSymbol method
            && IsOnConfiguring(method)
            && TryGetOptionsBuilderReceiver(invocation, out var receiver)
            && ReferencesParameter(receiver, method.Parameters[0])
        )
        {
            contextType = method.ContainingType.OriginalDefinition;
            return true;
        }

        contextType = null!;
        return false;
    }

    private static bool TryGetRegisteredContext(
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol contextType
    )
    {
        var callbackSyntax = syntax
            .Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        if (
            callbackSyntax == null
            || model.GetOperation(callbackSyntax, cancellationToken)
                is not IAnonymousFunctionOperation callback
            || !TryGetOptionsBuilderReceiver(invocation, out var receiver)
            || callback.Symbol.Parameters.Count(parameter =>
                IsDbContextOptionsBuilder(parameter.Type)
                && ReferencesParameter(receiver, parameter)
            ) != 1
        )
        {
            contextType = null!;
            return false;
        }

        var argumentSyntax = callbackSyntax.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
        if (
            argumentSyntax?.Parent?.Parent is not InvocationExpressionSyntax registrationSyntax
            || model.GetOperation(registrationSyntax, cancellationToken)
                is not IInvocationOperation registration
            || !TryGetEfRegistrationContext(registration.TargetMethod, out var registeredContext)
        )
        {
            contextType = null!;
            return false;
        }

        var callbackArgument = registration.Arguments.SingleOrDefault(argument =>
            argument.Syntax.Span == argumentSyntax.Span
        );
        var builderParameter = callback.Symbol.Parameters.Single(parameter =>
            IsDbContextOptionsBuilder(parameter.Type) && ReferencesParameter(receiver, parameter)
        );
        if (
            callbackArgument?.Parameter?.Name != "optionsAction"
            || !IsSupportedEfOptionsCallback(
                callbackArgument.Parameter.Type,
                builderParameter.Ordinal
            )
        )
        {
            contextType = null!;
            return false;
        }

        contextType = registeredContext.OriginalDefinition;
        return true;
    }

    private static bool TryGetEfRegistrationContext(
        IMethodSymbol method,
        out INamedTypeSymbol contextType
    )
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (
            !definition.IsExtensionMethod
            || definition.ContainingType.Name != "EntityFrameworkServiceCollectionExtensions"
            || definition.ContainingNamespace?.ToDisplayString()
                != "Microsoft.Extensions.DependencyInjection"
            || definition.Parameters.IsEmpty
            || !IsNamedType(
                definition.Parameters[0].Type,
                "Microsoft.Extensions.DependencyInjection",
                "IServiceCollection"
            )
            || !IsNamedType(
                definition.ReturnType,
                "Microsoft.Extensions.DependencyInjection",
                "IServiceCollection"
            )
        )
        {
            contextType = null!;
            return false;
        }

        var contextTypeArgument = definition.Name switch
        {
            "AddDbContext" or "AddDbContextPool" when method.TypeArguments.Length == 1 =>
                method.TypeArguments[0],
            "AddDbContext" or "AddDbContextPool" when method.TypeArguments.Length == 2 =>
                method.TypeArguments[1],
            "AddDbContextFactory" when method.TypeArguments.Length is 1 or 2 =>
                method.TypeArguments[0],
            "AddPooledDbContextFactory" when method.TypeArguments.Length == 1 =>
                method.TypeArguments[0],
            _ => null,
        };
        if (
            contextTypeArgument is not INamedTypeSymbol registeredContext
            || !LostUpdateOperationFacts.IsDbContextType(registeredContext)
        )
        {
            contextType = null!;
            return false;
        }

        contextType = registeredContext;
        return true;
    }

    private static bool IsSupportedEfOptionsCallback(ITypeSymbol type, int builderOrdinal)
    {
        if (
            type is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke }
            || !invoke.ReturnsVoid
            || builderOrdinal != invoke.Parameters.Length - 1
            || !IsDbContextOptionsBuilder(invoke.Parameters[builderOrdinal].Type)
        )
        {
            return false;
        }

        return invoke.Parameters.Length == 1
            || invoke.Parameters.Length == 2
                && IsNamedType(invoke.Parameters[0].Type, "System", "IServiceProvider");
    }

    private static bool IsNamedType(ITypeSymbol? type, string namespaceName, string name)
    {
        return type
                is INamedTypeSymbol
                {
                    Name: var typeName,
                    ContainingNamespace: { } containingNamespace,
                }
            && typeName == name
            && containingNamespace.ToDisplayString() == namespaceName;
    }

    private static bool TryGetDirectModelContext(
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol contextType
    )
    {
        var methodSyntax = syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (
            methodSyntax != null
            && model.GetDeclaredSymbol(methodSyntax, cancellationToken) is IMethodSymbol method
            && IsOnModelCreating(method)
            && invocation.Instance != null
            && ReferencesParameter(invocation.Instance, method.Parameters[0])
        )
        {
            contextType = method.ContainingType.OriginalDefinition;
            return true;
        }

        contextType = null!;
        return false;
    }

    private static bool TryGetEntityModelContext(
        IInvocationOperation invocation,
        InvocationExpressionSyntax syntax,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol contextType,
        out INamedTypeSymbol entityType
    )
    {
        var methodSyntax = syntax.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (
            methodSyntax != null
            && model.GetDeclaredSymbol(methodSyntax, cancellationToken) is IMethodSymbol method
            && IsOnModelCreating(method)
            && IsRealEfEntityTrackingMethod(invocation.TargetMethod)
            && (
                TryFindConfiguredEntity(
                    invocation,
                    method.Parameters[0],
                    model,
                    cancellationToken,
                    out entityType
                )
                || TryFindCallbackConfiguredEntity(
                    syntax,
                    method.Parameters[0],
                    model,
                    cancellationToken,
                    out entityType
                )
            )
        )
        {
            contextType = method.ContainingType.OriginalDefinition;
            return true;
        }

        contextType = null!;
        entityType = null!;
        return false;
    }

    private static bool TryGetOptionsBuilderReceiver(
        IInvocationOperation invocation,
        out IOperation receiver
    )
    {
        if (invocation.Instance != null && IsDbContextOptionsBuilder(invocation.Instance.Type))
        {
            receiver = invocation.Instance;
            return true;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (IsDbContextOptionsBuilder(argument.Value.Type))
            {
                receiver = argument.Value;
                return true;
            }
        }

        receiver = null!;
        return false;
    }

    private static bool ReferencesParameter(IOperation operation, IParameterSymbol parameter)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static bool IsDbContextOptionsBuilder(ITypeSymbol? type)
    {
        return type
                is INamedTypeSymbol
                {
                    Name: "DbContextOptionsBuilder",
                    ContainingNamespace: { } containingNamespace,
                }
            && containingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsRealEfOptionsMethod(IMethodSymbol method, string name)
    {
        return method.Name == name
            && method.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsRealEfModelMethod(IMethodSymbol method, string name)
    {
        return method.Name == name
            && method.ContainingType.Name == "ModelBuilder"
            && method.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsRealEfEntityTrackingMethod(IMethodSymbol method)
    {
        return method.Name == "HasChangeTrackingStrategy"
            && method.ContainingType
                is {
                    Name: "EntityTypeBuilder",
                    Arity: 1,
                    ContainingNamespace: { } containingNamespace,
                }
            && containingNamespace.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Metadata.Builders"
            && method.Parameters.Length == 1
            && method.Parameters[0].Type.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.ChangeTrackingStrategy";
    }

    private static bool TryGetEnumArgument(
        IInvocationOperation invocation,
        string enumType,
        out string memberName
    )
    {
        foreach (var argument in invocation.Arguments)
        {
            var value = LostUpdateOperationFacts.Unwrap(argument.Value);
            if (
                value is IFieldReferenceOperation field
                && field.Field.ContainingType.ToDisplayString() == enumType
            )
            {
                memberName = field.Field.Name;
                return true;
            }
        }

        memberName = string.Empty;
        return false;
    }

    private static bool TryGetOptionalBooleanArgument(
        IInvocationOperation invocation,
        out bool value
    )
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType != SpecialType.System_Boolean)
                continue;

            if (argument.Value.ConstantValue is { HasValue: true, Value: bool constant })
            {
                value = constant;
                return true;
            }

            value = false;
            return false;
        }

        value = true;
        return true;
    }

    private static bool IsOnConfiguring(IMethodSymbol method)
    {
        if (
            method.Name != "OnConfiguring"
            || !method.IsOverride
            || !method.ReturnsVoid
            || method.Parameters.Length != 1
            || !IsDbContextOptionsBuilder(method.Parameters[0].Type)
        )
        {
            return false;
        }

        for (
            var current = method.OverriddenMethod;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (
                current.ContainingType.Name == "DbContext"
                && current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore"
            )
            {
                return true;
            }
        }

        return false;
    }

    private ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
    > GetFluentConcurrencyModels(CancellationToken cancellationToken)
    {
        lock (_fluentScanGate)
        {
            var cached = _fluentConcurrencyModels;
            if (cached != null)
                return cached;

            var modelEvents = new Dictionary<INamedTypeSymbol, List<FluentModelEvent>>(
                SymbolEqualityComparer.Default
            );
            foreach (var tree in _compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = tree.GetRoot(cancellationToken);
                var model = _compilation.GetSemanticModel(tree);
                foreach (
                    var methodSyntax in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                )
                {
                    if (
                        model.GetDeclaredSymbol(methodSyntax, cancellationToken)
                            is IMethodSymbol method
                        && IsOnModelCreating(method)
                    )
                    {
                        GetOrAddEvents(modelEvents, method.ContainingType.OriginalDefinition);
                    }
                }

                var invocationSyntaxes = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .OrderBy(invocation => invocation.SpanStart)
                    .ThenBy(invocation => invocation.Span.Length);

                foreach (var invocationSyntax in invocationSyntaxes)
                {
                    var containingMethodSyntax = invocationSyntax
                        .Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();
                    if (
                        containingMethodSyntax == null
                        || model.GetDeclaredSymbol(containingMethodSyntax, cancellationToken)
                            is not IMethodSymbol containingMethod
                        || !IsOnModelCreating(containingMethod)
                        || model.GetOperation(invocationSyntax, cancellationToken)
                            is not IInvocationOperation invocation
                    )
                    {
                        continue;
                    }

                    if (HasUnsupportedNestedExecutable(invocationSyntax, model, cancellationToken))
                    {
                        continue;
                    }

                    var contextType = containingMethod.ContainingType.OriginalDefinition;
                    if (
                        IsBaseOnModelCreatingCall(invocationSyntax, invocation)
                        && !IsConditionallyExecuted(invocationSyntax, model, cancellationToken)
                    )
                    {
                        GetOrAddEvents(modelEvents, contextType)
                            .Add(
                                FluentModelEvent.CreateBaseCall(
                                    invocationSyntax.SpanStart,
                                    invocation.TargetMethod.ContainingType.OriginalDefinition
                                )
                            );
                        continue;
                    }

                    if (
                        invocation.TargetMethod.Name is "HasNoKey" or "HasKey"
                        && IsEfCoreBuilderMethod(invocation.TargetMethod)
                        && (
                            TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var keyEntity
                            )
                            || TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out keyEntity
                            )
                        )
                    )
                    {
                        var keyless = invocation.TargetMethod.Name == "HasNoKey";
                        var conditional = IsConditionallyExecuted(
                            invocationSyntax,
                            model,
                            cancellationToken
                        );
                        ImmutableHashSet<IPropertySymbol>? primaryKeys = null;
                        if (!conditional)
                        {
                            var keyBuilder = ImmutableHashSet.CreateBuilder<IPropertySymbol>(
                                SymbolEqualityComparer.Default
                            );
                            if (!keyless)
                            {
                                foreach (var argument in invocation.Arguments)
                                {
                                    CollectConfiguredKeyProperties(
                                        argument.Value,
                                        keyEntity,
                                        keyBuilder
                                    );
                                }
                            }

                            primaryKeys = keyBuilder.ToImmutable();
                        }

                        if (!conditional || !keyless)
                        {
                            GetOrAddEvents(modelEvents, contextType)
                                .Add(
                                    FluentModelEvent.CreateKeylessState(
                                        invocationSyntax.SpanStart,
                                        keyEntity.OriginalDefinition,
                                        keyless,
                                        primaryKeys
                                    )
                                );
                        }

                        continue;
                    }

                    if (
                        IsRealEfAlternateKeyMethod(invocation.TargetMethod)
                        && (
                            TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var alternateKeyEntity
                            )
                            || TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out alternateKeyEntity
                            )
                        )
                        && !IsConditionallyExecuted(invocationSyntax, model, cancellationToken)
                    )
                    {
                        var alternateKeys = ImmutableHashSet.CreateBuilder<IPropertySymbol>(
                            SymbolEqualityComparer.Default
                        );
                        foreach (var argument in invocation.Arguments)
                        {
                            CollectConfiguredKeyProperties(
                                argument.Value,
                                alternateKeyEntity,
                                alternateKeys
                            );
                        }

                        if (alternateKeys.Count > 0)
                        {
                            GetOrAddEvents(modelEvents, contextType)
                                .Add(
                                    FluentModelEvent.CreateAlternateKey(
                                        invocationSyntax.SpanStart,
                                        alternateKeyEntity.OriginalDefinition,
                                        alternateKeys.ToImmutable()
                                    )
                                );
                        }

                        continue;
                    }

                    if (
                        invocation.TargetMethod.Name == "Property"
                        && IsEfCoreBuilderMethod(invocation.TargetMethod)
                        && (
                            TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var mappedEntity
                            )
                            || TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out mappedEntity
                            )
                        )
                        && TryFindPropertyArgument(
                            invocation,
                            mappedEntity,
                            out var mappedProperty,
                            out var mappedPropertyName
                        )
                    )
                    {
                        GetOrAddEvents(modelEvents, contextType)
                            .Add(
                                FluentModelEvent.CreateMappedProperty(
                                    invocationSyntax.SpanStart,
                                    mappedEntity.OriginalDefinition,
                                    mappedProperty?.OriginalDefinition,
                                    mappedPropertyName,
                                    IsConditionallyExecuted(
                                        invocationSyntax,
                                        model,
                                        cancellationToken
                                    )
                                )
                            );

                        continue;
                    }

                    if (
                        invocation.TargetMethod.Name == "Ignore"
                        && IsEfCoreBuilderMethod(invocation.TargetMethod)
                        && (
                            TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var ignoredEntity
                            )
                            || TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out ignoredEntity
                            )
                        )
                        && TryFindPropertyArgument(
                            invocation,
                            ignoredEntity,
                            out var ignoredProperty,
                            out var ignoredPropertyName
                        )
                    )
                    {
                        if (!IsConditionallyExecuted(invocationSyntax, model, cancellationToken))
                        {
                            GetOrAddEvents(modelEvents, contextType)
                                .Add(
                                    FluentModelEvent.CreateIgnoredProperty(
                                        invocationSyntax.SpanStart,
                                        ignoredEntity.OriginalDefinition,
                                        ignoredProperty?.OriginalDefinition,
                                        ignoredPropertyName
                                    )
                                );
                        }

                        continue;
                    }

                    if (
                        TryGetStoreGeneratedState(invocation, out var storeGenerated)
                        && (
                            TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var generatedEntity
                            )
                            || TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out generatedEntity
                            )
                        )
                    )
                    {
                        IPropertySymbol? generatedProperty = null;
                        string? generatedPropertyName = null;
                        if (
                            !TryFindConfiguredProperty(
                                invocation,
                                model,
                                cancellationToken,
                                out generatedProperty
                            )
                        )
                        {
                            if (
                                !TryFindConfiguredPropertyName(
                                    invocation,
                                    model,
                                    cancellationToken,
                                    out generatedPropertyName
                                )
                            )
                            {
                                continue;
                            }

                            if (
                                TryFindPropertyOnEntityHierarchy(
                                    generatedEntity,
                                    generatedPropertyName,
                                    out generatedProperty
                                )
                            )
                            {
                                generatedPropertyName = null;
                            }
                        }

                        if (
                            IsConditionallyExecuted(invocationSyntax, model, cancellationToken)
                            && storeGenerated
                        )
                        {
                            continue;
                        }

                        GetOrAddEvents(modelEvents, contextType)
                            .Add(
                                FluentModelEvent.CreateStoreGeneratedState(
                                    invocationSyntax.SpanStart,
                                    generatedEntity.OriginalDefinition,
                                    generatedProperty?.OriginalDefinition,
                                    generatedPropertyName,
                                    storeGenerated
                                )
                            );
                        continue;
                    }

                    if (
                        invocation.TargetMethod.Name is not ("IsConcurrencyToken" or "IsRowVersion")
                        || !IsEfCoreBuilderMethod(invocation.TargetMethod)
                        || (
                            !TryFindConfiguredEntity(
                                invocation,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out var configuredEntity
                            )
                            && !TryFindCallbackConfiguredEntity(
                                invocationSyntax,
                                containingMethod.Parameters[0],
                                model,
                                cancellationToken,
                                out configuredEntity
                            )
                        )
                    )
                    {
                        continue;
                    }

                    IPropertySymbol? configuredProperty = null;
                    string? namedProperty = null;
                    if (
                        !TryFindConfiguredProperty(
                            invocation,
                            model,
                            cancellationToken,
                            out configuredProperty
                        )
                    )
                    {
                        if (
                            !TryFindConfiguredPropertyName(
                                invocation,
                                model,
                                cancellationToken,
                                out var propertyName
                            )
                        )
                            continue;

                        if (
                            !TryFindPropertyOnEntityHierarchy(
                                configuredEntity,
                                propertyName,
                                out configuredProperty
                            )
                        )
                        {
                            configuredProperty = null;
                            namedProperty = propertyName;
                        }
                    }

                    var enabled = ProvesConcurrencyEnabled(invocation);
                    bool? rowVersion =
                        invocation.TargetMethod.Name == "IsRowVersion" ? true
                        : enabled ? null
                        : false;
                    if (
                        IsConditionallyExecuted(invocationSyntax, model, cancellationToken)
                        && enabled
                    )
                        continue;

                    GetOrAddEvents(modelEvents, contextType)
                        .Add(
                            FluentModelEvent.CreatePropertyState(
                                invocationSyntax.SpanStart,
                                configuredEntity.OriginalDefinition,
                                configuredProperty?.OriginalDefinition,
                                namedProperty,
                                enabled,
                                rowVersion
                            )
                        );
                }
            }

            var builder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
            >(SymbolEqualityComparer.Default);
            var building = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                builder[contextType] = BuildEffectiveFluentModel(
                    contextType,
                    modelEvents,
                    builder,
                    building
                );
            }

            var storeGeneratedBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
            >(SymbolEqualityComparer.Default);
            var buildingStoreGenerated = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in modelEvents.Keys)
            {
                storeGeneratedBuilder[contextType] = BuildEffectiveStoreGeneratedModel(
                    contextType,
                    modelEvents,
                    storeGeneratedBuilder,
                    buildingStoreGenerated
                );
            }

            _fluentStoreGeneratedModels = storeGeneratedBuilder.ToImmutable();

            var rowVersionBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
            >(SymbolEqualityComparer.Default);
            var buildingRowVersions = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                rowVersionBuilder[contextType] = BuildEffectiveRowVersionModel(
                    contextType,
                    modelEvents,
                    rowVersionBuilder,
                    buildingRowVersions
                );
            }

            _fluentRowVersionModels = rowVersionBuilder.ToImmutable();

            var namedBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
            >(SymbolEqualityComparer.Default);
            var buildingNamed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                namedBuilder[contextType] = BuildEffectiveNamedConcurrencyModel(
                    contextType,
                    modelEvents,
                    namedBuilder,
                    buildingNamed
                );
            }

            _fluentNamedConcurrencyModels = namedBuilder.ToImmutable();

            var namedStoreGeneratedBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
            >(SymbolEqualityComparer.Default);
            var buildingNamedStoreGenerated = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in modelEvents.Keys)
            {
                namedStoreGeneratedBuilder[contextType] = BuildEffectiveNamedStoreGeneratedModel(
                    contextType,
                    modelEvents,
                    namedStoreGeneratedBuilder,
                    buildingNamedStoreGenerated
                );
            }

            _fluentNamedStoreGeneratedModels = namedStoreGeneratedBuilder.ToImmutable();

            var keylessBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, bool>
            >(SymbolEqualityComparer.Default);
            var buildingKeyless = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                keylessBuilder[contextType] = BuildEffectiveKeylessModel(
                    contextType,
                    modelEvents,
                    keylessBuilder,
                    buildingKeyless
                );
            }

            _fluentKeylessModels = keylessBuilder.ToImmutable();

            var primaryKeyBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
            >(SymbolEqualityComparer.Default);
            var buildingPrimaryKeys = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                primaryKeyBuilder[contextType] = BuildEffectivePrimaryKeyModel(
                    contextType,
                    modelEvents,
                    primaryKeyBuilder,
                    buildingPrimaryKeys
                );
            }

            _fluentPrimaryKeyModels = primaryKeyBuilder.ToImmutable();
            var alternateKeyBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
            >(SymbolEqualityComparer.Default);
            var buildingAlternateKeys = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in modelEvents.Keys)
            {
                alternateKeyBuilder[contextType] = BuildEffectiveAlternateKeyModel(
                    contextType,
                    modelEvents,
                    alternateKeyBuilder,
                    buildingAlternateKeys
                );
            }

            _fluentAlternateKeyModels = alternateKeyBuilder.ToImmutable();

            var ignoredBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
            >(SymbolEqualityComparer.Default);
            var buildingIgnored = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                ignoredBuilder[contextType] = BuildEffectiveIgnoredProperties(
                    contextType,
                    modelEvents,
                    ignoredBuilder,
                    buildingIgnored
                );
            }

            _fluentIgnoredProperties = ignoredBuilder.ToImmutable();

            var definitelyIgnoredBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
            >(SymbolEqualityComparer.Default);
            var buildingDefinitelyIgnored = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in modelEvents.Keys)
            {
                definitelyIgnoredBuilder[contextType] = BuildEffectiveIgnoredProperties(
                    contextType,
                    modelEvents,
                    definitelyIgnoredBuilder,
                    buildingDefinitelyIgnored,
                    retainConditionallyRemapped: true
                );
            }

            _fluentDefinitelyIgnoredProperties = definitelyIgnoredBuilder.ToImmutable();

            var ignoredNameBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>>
            >(SymbolEqualityComparer.Default);
            var buildingIgnoredNames = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (var contextType in modelEvents.Keys)
            {
                ignoredNameBuilder[contextType] = BuildEffectiveIgnoredPropertyNames(
                    contextType,
                    modelEvents,
                    ignoredNameBuilder,
                    buildingIgnoredNames
                );
            }

            _fluentIgnoredPropertyNames = ignoredNameBuilder.ToImmutable();

            var mappedBuilder = ImmutableDictionary.CreateBuilder<
                INamedTypeSymbol,
                ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
            >(SymbolEqualityComparer.Default);
            var buildingMapped = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var contextType in modelEvents.Keys)
            {
                mappedBuilder[contextType] = BuildEffectiveMappedProperties(
                    contextType,
                    modelEvents,
                    mappedBuilder,
                    buildingMapped
                );
            }

            _fluentMappedProperties = mappedBuilder.ToImmutable();

            cached = builder.ToImmutable();
            _fluentConcurrencyModels = cached;
            return cached;
        }
    }

    private static List<FluentModelEvent> GetOrAddEvents(
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        INamedTypeSymbol contextType
    )
    {
        if (!modelEvents.TryGetValue(contextType, out var events))
        {
            events = new List<FluentModelEvent>();
            modelEvents.Add(contextType, events);
        }

        return events;
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<IPropertySymbol, bool>
    > BuildEffectiveFluentModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<
                INamedTypeSymbol,
                ImmutableDictionary<IPropertySymbol, bool>
            >(SymbolEqualityComparer.Default);
        }

        var states = new Dictionary<INamedTypeSymbol, Dictionary<IPropertySymbol, bool>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    var baseModel = BuildEffectiveFluentModel(
                        modelEvent.BaseContextType,
                        modelEvents,
                        cache,
                        building
                    );
                    foreach (var entityState in baseModel)
                    {
                        var propertyStates = GetOrAddPropertyStates(states, entityState.Key);
                        foreach (var propertyState in entityState.Value)
                            propertyStates[propertyState.Key] = propertyState.Value;
                    }

                    continue;
                }

                if (modelEvent.Keyless.HasValue || modelEvent.Property == null)
                    continue;

                var configuredStates = GetOrAddPropertyStates(states, modelEvent.EntityType!);
                configuredStates[modelEvent.Property!] = modelEvent.Enabled;
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableDictionary<IPropertySymbol, bool>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = entityState.Value.ToImmutableDictionary(
                SymbolEqualityComparer.Default
            );
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<IPropertySymbol, bool>
    > BuildEffectiveStoreGeneratedModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<
                INamedTypeSymbol,
                ImmutableDictionary<IPropertySymbol, bool>
            >(SymbolEqualityComparer.Default);
        }

        var states = new Dictionary<INamedTypeSymbol, Dictionary<IPropertySymbol, bool>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    var baseModel = BuildEffectiveStoreGeneratedModel(
                        modelEvent.BaseContextType,
                        modelEvents,
                        cache,
                        building
                    );
                    foreach (var entityState in baseModel)
                    {
                        var propertyStates = GetOrAddPropertyStates(states, entityState.Key);
                        foreach (var propertyState in entityState.Value)
                            propertyStates[propertyState.Key] = propertyState.Value;
                    }

                    continue;
                }

                var property = modelEvent.StoreGeneratedProperty ?? modelEvent.IgnoredProperty;
                if (property == null)
                    continue;

                var configuredStates = GetOrAddPropertyStates(states, modelEvent.EntityType!);
                configuredStates[property] = modelEvent.StoreGenerated ?? false;
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableDictionary<IPropertySymbol, bool>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = entityState.Value.ToImmutableDictionary(
                SymbolEqualityComparer.Default
            );
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<IPropertySymbol, bool>
    > BuildEffectiveRowVersionModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<
                INamedTypeSymbol,
                ImmutableDictionary<IPropertySymbol, bool>
            >(SymbolEqualityComparer.Default);
        }

        var states = new Dictionary<INamedTypeSymbol, Dictionary<IPropertySymbol, bool>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    var baseModel = BuildEffectiveRowVersionModel(
                        modelEvent.BaseContextType,
                        modelEvents,
                        cache,
                        building
                    );
                    foreach (var entityState in baseModel)
                    {
                        var propertyStates = GetOrAddPropertyStates(states, entityState.Key);
                        foreach (var propertyState in entityState.Value)
                            propertyStates[propertyState.Key] = propertyState.Value;
                    }

                    continue;
                }

                if (modelEvent.Property == null || !modelEvent.RowVersion.HasValue)
                {
                    continue;
                }

                var configuredStates = GetOrAddPropertyStates(states, modelEvent.EntityType!);
                configuredStates[modelEvent.Property] = modelEvent.RowVersion.Value;
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableDictionary<IPropertySymbol, bool>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = entityState.Value.ToImmutableDictionary(
                SymbolEqualityComparer.Default
            );
        }

        return builder.ToImmutable();
    }

    private static Dictionary<IPropertySymbol, bool> GetOrAddPropertyStates(
        Dictionary<INamedTypeSymbol, Dictionary<IPropertySymbol, bool>> states,
        INamedTypeSymbol entityType
    )
    {
        if (!states.TryGetValue(entityType, out var propertyStates))
        {
            propertyStates = new Dictionary<IPropertySymbol, bool>(SymbolEqualityComparer.Default);
            states.Add(entityType, propertyStates);
        }

        return propertyStates;
    }

    private static Dictionary<IPropertySymbol, bool> GetEffectivePropertyStates(
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<IPropertySymbol, bool>>
        > models,
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType
    )
    {
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<IPropertySymbol, bool>
        >? entityModels = null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (models.TryGetValue(current.OriginalDefinition, out entityModels))
                break;
        }

        var effectiveStates = new Dictionary<IPropertySymbol, bool>(SymbolEqualityComparer.Default);
        if (entityModels == null)
            return effectiveStates;

        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
            hierarchy.Push(current.OriginalDefinition);

        while (hierarchy.Count > 0)
        {
            if (!entityModels.TryGetValue(hierarchy.Pop(), out var propertyStates))
                continue;

            foreach (var propertyState in propertyStates)
                effectiveStates[propertyState.Key] = propertyState.Value;
        }

        return effectiveStates;
    }

    private bool HasFluentRowVersionProtection(
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType,
        ISymbol contextSymbol,
        CancellationToken cancellationToken
    )
    {
        GetFluentConcurrencyModels(cancellationToken);
        if (_fluentRowVersionModels == null || _fluentStoreGeneratedModels == null)
            return false;

        var storeGeneratedStates = GetEffectivePropertyStates(
            _fluentStoreGeneratedModels,
            contextType,
            entityType
        );
        return GetEffectivePropertyStates(_fluentRowVersionModels, contextType, entityType)
            .Any(propertyState =>
                propertyState.Value
                && storeGeneratedStates.TryGetValue(propertyState.Key, out var storeGenerated)
                && storeGenerated
                && !IsDefinitelyIgnoredPropertyForConcurrency(
                    entityType,
                    propertyState.Key,
                    contextSymbol,
                    cancellationToken
                )
            );
    }

    private bool HasNamedConcurrencyProtection(
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType,
        CancellationToken cancellationToken
    )
    {
        GetFluentConcurrencyModels(cancellationToken);
        if (_fluentNamedConcurrencyModels == null || _fluentNamedStoreGeneratedModels == null)
        {
            return false;
        }

        var rowVersionStates = GetEffectiveNamedPropertyStates(
            _fluentNamedConcurrencyModels,
            contextType,
            entityType
        );
        var storeGeneratedStates = GetEffectiveNamedPropertyStates(
            _fluentNamedStoreGeneratedModels,
            contextType,
            entityType
        );

        return rowVersionStates.Any(propertyState =>
            propertyState.Value
            && storeGeneratedStates.TryGetValue(propertyState.Key, out var storeGenerated)
            && storeGenerated
            && !IsIgnoredPropertyName(contextType, entityType, propertyState.Key)
        );
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<string, bool>
    > BuildEffectiveNamedConcurrencyModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        return BuildEffectiveNamedBooleanModel(
            contextType,
            modelEvents,
            cache,
            building,
            storeGenerated: false
        );
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<string, bool>
    > BuildEffectiveNamedStoreGeneratedModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        return BuildEffectiveNamedBooleanModel(
            contextType,
            modelEvents,
            cache,
            building,
            storeGenerated: true
        );
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableDictionary<string, bool>
    > BuildEffectiveNamedBooleanModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building,
        bool storeGenerated
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableDictionary<string, bool>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, Dictionary<string, bool>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveNamedBooleanModel(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building,
                            storeGenerated
                        )
                    )
                    {
                        if (!states.TryGetValue(entityState.Key, out var propertyStates))
                        {
                            propertyStates = new Dictionary<string, bool>(StringComparer.Ordinal);
                            states.Add(entityState.Key, propertyStates);
                        }

                        foreach (var propertyState in entityState.Value)
                            propertyStates[propertyState.Key] = propertyState.Value;
                    }

                    continue;
                }

                var propertyName = storeGenerated
                    ? modelEvent.StoreGeneratedPropertyName ?? modelEvent.IgnoredPropertyName
                    : modelEvent.PropertyName;
                bool? propertyValue = storeGenerated
                    ? modelEvent.StoreGenerated
                        ?? (modelEvent.IgnoredPropertyName != null ? false : null)
                    : modelEvent.RowVersion;
                if (propertyName == null || !propertyValue.HasValue)
                    continue;

                if (!states.TryGetValue(modelEvent.EntityType!, out var configuredStates))
                {
                    configuredStates = new Dictionary<string, bool>(StringComparer.Ordinal);
                    states.Add(modelEvent.EntityType!, configuredStates);
                }

                configuredStates[propertyName] = propertyValue.Value;
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableDictionary<string, bool>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = entityState.Value.ToImmutableDictionary(
                StringComparer.Ordinal
            );
        }

        return builder.ToImmutable();
    }

    private static Dictionary<string, bool> GetEffectiveNamedPropertyStates(
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>
        > models,
        INamedTypeSymbol contextType,
        INamedTypeSymbol entityType
    )
    {
        ImmutableDictionary<INamedTypeSymbol, ImmutableDictionary<string, bool>>? entityModels =
            null;
        for (INamedTypeSymbol? current = contextType; current != null; current = current.BaseType)
        {
            if (models.TryGetValue(current.OriginalDefinition, out entityModels))
                break;
        }

        var effectiveStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (entityModels == null)
            return effectiveStates;

        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = entityType; current != null; current = current.BaseType)
            hierarchy.Push(current.OriginalDefinition);

        while (hierarchy.Count > 0)
        {
            if (!entityModels.TryGetValue(hierarchy.Pop(), out var propertyStates))
                continue;

            foreach (var propertyState in propertyStates)
                effectiveStates[propertyState.Key] = propertyState.Value;
        }

        return effectiveStates;
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableHashSet<IPropertySymbol>
    > BuildEffectiveIgnoredProperties(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building,
        bool retainConditionallyRemapped = false
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, HashSet<IPropertySymbol>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveIgnoredProperties(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building,
                            retainConditionallyRemapped
                        )
                    )
                    {
                        if (!states.TryGetValue(entityState.Key, out var ignoredProperties))
                        {
                            ignoredProperties = new HashSet<IPropertySymbol>(
                                SymbolEqualityComparer.Default
                            );
                            states.Add(entityState.Key, ignoredProperties);
                        }

                        ignoredProperties.UnionWith(entityState.Value);
                    }

                    continue;
                }

                if (modelEvent.MappedProperty != null)
                {
                    if (retainConditionallyRemapped && modelEvent.MappedPropertyIsConditional)
                    {
                        continue;
                    }

                    if (states.TryGetValue(modelEvent.EntityType!, out var mappedProperties))
                    {
                        mappedProperties.Remove(modelEvent.MappedProperty);
                    }

                    continue;
                }

                if (modelEvent.IgnoredProperty == null)
                    continue;

                if (!states.TryGetValue(modelEvent.EntityType!, out var configuredProperties))
                {
                    configuredProperties = new HashSet<IPropertySymbol>(
                        SymbolEqualityComparer.Default
                    );
                    states.Add(modelEvent.EntityType!, configuredProperties);
                }

                configuredProperties.Add(modelEvent.IgnoredProperty);
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableHashSet<IPropertySymbol>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = ImmutableHashSet.CreateRange<IPropertySymbol>(
                SymbolEqualityComparer.Default,
                entityState.Value
            );
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableHashSet<string>
    > BuildEffectiveIgnoredPropertyNames(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableHashSet<string>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, HashSet<string>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveIgnoredPropertyNames(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building
                        )
                    )
                    {
                        if (!states.TryGetValue(entityState.Key, out var ignoredNames))
                        {
                            ignoredNames = new HashSet<string>(StringComparer.Ordinal);
                            states.Add(entityState.Key, ignoredNames);
                        }

                        ignoredNames.UnionWith(entityState.Value);
                    }

                    continue;
                }

                if (modelEvent.MappedPropertyName != null)
                {
                    if (states.TryGetValue(modelEvent.EntityType!, out var mappedNames))
                    {
                        mappedNames.Remove(modelEvent.MappedPropertyName);
                    }

                    continue;
                }

                if (modelEvent.IgnoredPropertyName == null)
                    continue;

                if (!states.TryGetValue(modelEvent.EntityType!, out var configuredNames))
                {
                    configuredNames = new HashSet<string>(StringComparer.Ordinal);
                    states.Add(modelEvent.EntityType!, configuredNames);
                }

                configuredNames.Add(modelEvent.IgnoredPropertyName);
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, ImmutableHashSet<string>>(
            SymbolEqualityComparer.Default
        );
        foreach (var entityState in states)
        {
            builder[entityState.Key] = entityState.Value.ToImmutableHashSet(StringComparer.Ordinal);
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableHashSet<IPropertySymbol>
    > BuildEffectiveMappedProperties(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, HashSet<IPropertySymbol>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveMappedProperties(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building
                        )
                    )
                    {
                        if (!states.TryGetValue(entityState.Key, out var mappedProperties))
                        {
                            mappedProperties = new HashSet<IPropertySymbol>(
                                SymbolEqualityComparer.Default
                            );
                            states.Add(entityState.Key, mappedProperties);
                        }

                        mappedProperties.UnionWith(entityState.Value);
                    }

                    continue;
                }

                if (modelEvent.IgnoredProperty != null)
                {
                    if (states.TryGetValue(modelEvent.EntityType!, out var ignoredProperties))
                    {
                        ignoredProperties.Remove(modelEvent.IgnoredProperty);
                    }

                    continue;
                }

                if (modelEvent.MappedProperty == null)
                    continue;

                if (!states.TryGetValue(modelEvent.EntityType!, out var configuredProperties))
                {
                    configuredProperties = new HashSet<IPropertySymbol>(
                        SymbolEqualityComparer.Default
                    );
                    states.Add(modelEvent.EntityType!, configuredProperties);
                }

                configuredProperties.Add(modelEvent.MappedProperty);
            }
        }

        building.Remove(contextType);
        var builder = ImmutableDictionary.CreateBuilder<
            INamedTypeSymbol,
            ImmutableHashSet<IPropertySymbol>
        >(SymbolEqualityComparer.Default);
        foreach (var entityState in states)
        {
            builder[entityState.Key] = ImmutableHashSet.CreateRange<IPropertySymbol>(
                SymbolEqualityComparer.Default,
                entityState.Value
            );
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<INamedTypeSymbol, bool> BuildEffectiveKeylessModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, bool>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, bool>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveKeylessModel(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building
                        )
                    )
                    {
                        states[entityState.Key] = entityState.Value;
                    }

                    continue;
                }

                if (modelEvent.Keyless.HasValue)
                    states[modelEvent.EntityType!] = modelEvent.Keyless.Value;
            }
        }

        building.Remove(contextType);
        return states.ToImmutableDictionary(SymbolEqualityComparer.Default);
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableHashSet<IPropertySymbol>
    > BuildEffectivePrimaryKeyModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectivePrimaryKeyModel(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building
                        )
                    )
                    {
                        states[entityState.Key] = entityState.Value;
                    }

                    continue;
                }

                if (modelEvent.PrimaryKeys != null)
                    states[modelEvent.EntityType!] = modelEvent.PrimaryKeys;
            }
        }

        building.Remove(contextType);
        return states.ToImmutableDictionary(SymbolEqualityComparer.Default);
    }

    private static ImmutableDictionary<
        INamedTypeSymbol,
        ImmutableHashSet<IPropertySymbol>
    > BuildEffectiveAlternateKeyModel(
        INamedTypeSymbol contextType,
        Dictionary<INamedTypeSymbol, List<FluentModelEvent>> modelEvents,
        ImmutableDictionary<
            INamedTypeSymbol,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>
        >.Builder cache,
        HashSet<INamedTypeSymbol> building
    )
    {
        if (cache.TryGetValue(contextType, out var cached))
            return cached;

        if (!building.Add(contextType))
        {
            return ImmutableDictionary.Create<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
                SymbolEqualityComparer.Default
            );
        }

        var states = new Dictionary<INamedTypeSymbol, ImmutableHashSet<IPropertySymbol>>(
            SymbolEqualityComparer.Default
        );
        if (modelEvents.TryGetValue(contextType, out var events))
        {
            foreach (var modelEvent in events.OrderBy(item => item.Position))
            {
                if (modelEvent.BaseContextType != null)
                {
                    foreach (
                        var entityState in BuildEffectiveAlternateKeyModel(
                            modelEvent.BaseContextType,
                            modelEvents,
                            cache,
                            building
                        )
                    )
                    {
                        states[entityState.Key] = states.TryGetValue(
                            entityState.Key,
                            out var configuredKeys
                        )
                            ? configuredKeys.Union(entityState.Value)
                            : entityState.Value;
                    }

                    continue;
                }

                if (modelEvent.AlternateKeys == null)
                    continue;

                states[modelEvent.EntityType!] = states.TryGetValue(
                    modelEvent.EntityType!,
                    out var existingKeys
                )
                    ? existingKeys.Union(modelEvent.AlternateKeys)
                    : modelEvent.AlternateKeys;
            }
        }

        building.Remove(contextType);
        return states.ToImmutableDictionary(SymbolEqualityComparer.Default);
    }

    private static bool IsBaseOnModelCreatingCall(
        InvocationExpressionSyntax syntax,
        IInvocationOperation invocation
    )
    {
        return syntax.Expression
                is MemberAccessExpressionSyntax
                {
                    Expression: BaseExpressionSyntax,
                    Name.Identifier.ValueText: "OnModelCreating",
                }
            && invocation.TargetMethod.Name == "OnModelCreating"
            && invocation.TargetMethod.Parameters.Length == 1
            && LostUpdateOperationFacts.IsDbContextType(invocation.TargetMethod.ContainingType);
    }

    private static bool TryGetContextType(ISymbol contextSymbol, out INamedTypeSymbol contextType)
    {
        ITypeSymbol? type = contextSymbol switch
        {
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            INamedTypeSymbol namedType => namedType,
            _ => null,
        };

        if (type is INamedTypeSymbol namedContext && LostUpdateOperationFacts.IsDbContextType(type))
        {
            contextType = namedContext;
            return true;
        }

        contextType = null!;
        return false;
    }

    private static bool IsOnModelCreating(IMethodSymbol method)
    {
        if (
            method.Name != "OnModelCreating"
            || !method.IsOverride
            || !method.ReturnsVoid
            || method.Parameters.Length != 1
            || method.Parameters[0].Type
                is not INamedTypeSymbol
                {
                    Name: "ModelBuilder",
                    ContainingNamespace: { } modelBuilderNamespace,
                }
            || modelBuilderNamespace.ToDisplayString() != "Microsoft.EntityFrameworkCore"
        )
        {
            return false;
        }

        for (
            var current = method.OverriddenMethod;
            current != null;
            current = current.OverriddenMethod
        )
        {
            if (
                current.ContainingType.Name == "DbContext"
                && current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore"
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnsupportedNestedExecutable(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax)
                return false;

            if (ancestor is LocalFunctionStatementSyntax)
                return true;

            if (ancestor is not AnonymousFunctionExpressionSyntax anonymousFunction)
                continue;

            if (
                anonymousFunction.Parent
                    is ArgumentSyntax
                    {
                        Parent.Parent: InvocationExpressionSyntax callbackInvocation,
                    }
                && model.GetOperation(callbackInvocation, cancellationToken)
                    is IInvocationOperation callback
                && IsModelBuilderEntityMethod(callback.TargetMethod)
            )
            {
                continue;
            }

            return true;
        }

        return true;
    }

    private static bool TryFindCallbackConfiguredEntity(
        InvocationExpressionSyntax invocationSyntax,
        IParameterSymbol modelBuilderParameter,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol entityType
    )
    {
        foreach (
            var callbackSyntax in invocationSyntax
                .Ancestors()
                .OfType<AnonymousFunctionExpressionSyntax>()
        )
        {
            if (
                callbackSyntax.Parent
                    is not ArgumentSyntax
                    {
                        Parent.Parent: InvocationExpressionSyntax callbackInvocationSyntax,
                    }
                || model.GetOperation(callbackInvocationSyntax, cancellationToken)
                    is not IInvocationOperation callbackInvocation
                || callbackInvocation.TargetMethod.TypeArguments.Length != 1
                || callbackInvocation.TargetMethod.TypeArguments[0]
                    is not INamedTypeSymbol configuredType
                || !IsModelBuilderEntityMethod(callbackInvocation.TargetMethod)
                || !IsBoundToModelBuilderParameter(
                    callbackInvocation,
                    modelBuilderParameter,
                    model,
                    cancellationToken
                )
                || model.GetOperation(callbackSyntax, cancellationToken)
                    is not IAnonymousFunctionOperation callbackOperation
                || callbackOperation.Symbol.Parameters.Length != 1
                || model.GetOperation(invocationSyntax, cancellationToken)
                    is not IInvocationOperation configuredInvocation
                || configuredInvocation.Instance == null
                || !IsStableEfBuilderReference(
                    configuredInvocation.Instance,
                    callbackOperation.Symbol.Parameters[0],
                    model,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    invocationSyntax.SpanStart,
                    cancellationToken
                )
            )
            {
                continue;
            }

            entityType = configuredType;
            return true;
        }

        entityType = null!;
        return false;
    }

    private static bool TryFindConfiguredProperty(
        IInvocationOperation invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out IPropertySymbol property
    )
    {
        return TryFindConfiguredProperty(
            GetInvocationReceiver(invocation),
            model,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            invocation.Syntax.SpanStart,
            cancellationToken,
            out property
        );
    }

    private static bool TryFindConfiguredPropertyName(
        IInvocationOperation invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out string propertyName
    )
    {
        return TryFindConfiguredPropertyName(
            GetInvocationReceiver(invocation),
            model,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            invocation.Syntax.SpanStart,
            cancellationToken,
            out propertyName
        );
    }

    private static bool TryFindPropertyArgument(
        IInvocationOperation invocation,
        INamedTypeSymbol entityType,
        out IPropertySymbol? property,
        out string? propertyName
    )
    {
        foreach (var argument in invocation.Arguments)
        {
            if (FindPropertyReference(argument.Value) is { } referencedProperty)
            {
                property = referencedProperty;
                propertyName = null;
                return true;
            }

            if (
                argument.Parameter?.Type.SpecialType == SpecialType.System_String
                && argument.Value.ConstantValue is { HasValue: true, Value: string name }
                && !string.IsNullOrWhiteSpace(name)
            )
            {
                property = TryFindPropertyOnEntityHierarchy(entityType, name, out var namedProperty)
                    ? namedProperty
                    : null;
                propertyName = property == null ? name : null;
                return true;
            }
        }

        property = null;
        propertyName = null;
        return false;
    }

    private static IPropertySymbol? FindPropertyReference(IOperation operation)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (operation is IPropertyReferenceOperation propertyReference)
            return propertyReference.Property;

        foreach (var child in operation.ChildOperations)
        {
            var property = FindPropertyReference(child);
            if (property != null)
                return property;
        }

        return null;
    }

    private static bool IsConditionallyExecuted(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        var flowInvocation = invocation;
        var callback = invocation
            .Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        if (callback != null)
        {
            if (IsConditionallyExecutedInCallback(invocation, callback, model, cancellationToken))
            {
                return true;
            }

            if (
                callback.Parent
                is not ArgumentSyntax
                {
                    Parent.Parent: InvocationExpressionSyntax callbackInvocation,
                }
            )
            {
                return true;
            }

            flowInvocation = callbackInvocation;
        }

        var methodSyntax = flowInvocation
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        return methodSyntax == null
            || IsConditionallyExecutedWithin(
                flowInvocation,
                methodSyntax,
                model,
                cancellationToken
            );
    }

    private static bool IsConditionallyExecutedInCallback(
        InvocationExpressionSyntax invocation,
        AnonymousFunctionExpressionSyntax callback,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            if (ReferenceEquals(ancestor, callback))
                return false;

            switch (ancestor)
            {
                case IfStatementSyntax ifStatement:
                    if (ifStatement.Condition.Span.Contains(invocation.Span))
                        continue;

                    if (
                        !TryGetConstantBoolean(
                            ifStatement.Condition,
                            model,
                            cancellationToken,
                            out var ifCondition
                        )
                    )
                    {
                        return true;
                    }

                    var selectedStatement = ifCondition
                        ? ifStatement.Statement
                        : ifStatement.Else?.Statement;
                    if (
                        selectedStatement == null
                        || !selectedStatement.Span.Contains(invocation.Span)
                    )
                    {
                        return true;
                    }
                    break;

                case ConditionalExpressionSyntax conditional:
                    if (conditional.Condition.Span.Contains(invocation.Span))
                        continue;

                    if (
                        !TryGetConstantBoolean(
                            conditional.Condition,
                            model,
                            cancellationToken,
                            out var conditionalValue
                        )
                    )
                    {
                        return true;
                    }

                    var selectedExpression = conditionalValue
                        ? conditional.WhenTrue
                        : conditional.WhenFalse;
                    if (!selectedExpression.Span.Contains(invocation.Span))
                        return true;
                    break;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.LogicalAndExpression)
                        || binary.IsKind(SyntaxKind.LogicalOrExpression):
                    if (binary.Left.Span.Contains(invocation.Span))
                        continue;

                    if (
                        !TryGetConstantBoolean(
                            binary.Left,
                            model,
                            cancellationToken,
                            out var leftValue
                        )
                    )
                    {
                        return true;
                    }

                    var evaluatesRight = binary.IsKind(SyntaxKind.LogicalAndExpression)
                        ? leftValue
                        : !leftValue;
                    if (!evaluatesRight)
                        return true;
                    break;

                case SwitchExpressionArmSyntax switchArm:
                    if (!IsSelectedSwitchArm(switchArm, model, cancellationToken))
                        return true;
                    break;

                case SwitchSectionSyntax switchSection:
                    if (!IsSelectedSwitchSection(switchSection, model, cancellationToken))
                        return true;
                    break;

                case ForStatementSyntax
                or CommonForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or CatchClauseSyntax:
                    return true;
            }
        }

        return true;
    }

    private static bool TryGetConstantBoolean(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        out bool value
    )
    {
        var constant = model.GetConstantValue(expression, cancellationToken);
        if (constant is { HasValue: true, Value: bool boolean })
        {
            value = boolean;
            return true;
        }

        value = false;
        return false;
    }

    private static bool IsSelectedSwitchArm(
        SwitchExpressionArmSyntax arm,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        if (arm.Parent is not SwitchExpressionSyntax switchExpression)
            return false;

        var governingValue = model.GetConstantValue(
            switchExpression.GoverningExpression,
            cancellationToken
        );
        if (!governingValue.HasValue)
            return false;

        foreach (var candidate in switchExpression.Arms)
        {
            if (
                candidate.WhenClause != null
                || !PatternMatchesConstant(
                    candidate.Pattern,
                    governingValue.Value,
                    model,
                    cancellationToken
                )
            )
            {
                continue;
            }

            return ReferenceEquals(candidate, arm);
        }

        return false;
    }

    private static bool IsSelectedSwitchSection(
        SwitchSectionSyntax section,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        if (section.Parent is not SwitchStatementSyntax switchStatement)
            return false;

        var governingValue = model.GetConstantValue(switchStatement.Expression, cancellationToken);
        if (!governingValue.HasValue)
            return false;

        SwitchSectionSyntax? defaultSection = null;
        foreach (var candidate in switchStatement.Sections)
        {
            foreach (var label in candidate.Labels)
            {
                if (label is DefaultSwitchLabelSyntax)
                {
                    defaultSection = candidate;
                    continue;
                }

                if (
                    label is CaseSwitchLabelSyntax caseLabel
                    && ConstantsEqual(
                        model.GetConstantValue(caseLabel.Value, cancellationToken),
                        governingValue.Value
                    )
                )
                {
                    return ReferenceEquals(candidate, section);
                }
            }
        }

        return ReferenceEquals(defaultSection, section);
    }

    private static bool PatternMatchesConstant(
        PatternSyntax pattern,
        object? governingValue,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        return pattern is DiscardPatternSyntax
            || pattern is ConstantPatternSyntax constantPattern
                && ConstantsEqual(
                    model.GetConstantValue(constantPattern.Expression, cancellationToken),
                    governingValue
                );
    }

    private static bool ConstantsEqual(Optional<object?> candidate, object? governingValue)
    {
        return candidate.HasValue && Equals(candidate.Value, governingValue);
    }

    private static bool IsConditionallyExecutedWithin(
        InvocationExpressionSyntax invocation,
        SyntaxNode executable,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ControlFlowGraph? flowGraph;
            var executableOperation = model.GetOperation(executable, cancellationToken);
            if (executableOperation is IMethodBodyOperation methodBody)
            {
                flowGraph = ControlFlowGraph.Create(methodBody, cancellationToken);
            }
            else if (executableOperation is IAnonymousFunctionOperation callback)
            {
                flowGraph = ControlFlowGraph.Create(callback.Body, cancellationToken);
            }
            else
            {
                flowGraph = ControlFlowGraph.Create(executable, model, cancellationToken);
            }
            if (flowGraph == null)
                return true;

            var invocationBlock = FindContainingBlock(flowGraph, invocation);
            var exitBlock = flowGraph.Blocks.FirstOrDefault(block =>
                block.Kind == BasicBlockKind.Exit
            );
            return invocationBlock == null
                || !invocationBlock.IsReachable
                || exitBlock == null
                || CanReachAvoiding(flowGraph.Blocks[0], exitBlock, invocationBlock);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static BasicBlock? FindContainingBlock(
        ControlFlowGraph flowGraph,
        InvocationExpressionSyntax invocation
    )
    {
        foreach (var block in flowGraph.Blocks)
        {
            foreach (var operation in block.Operations)
            {
                if (
                    operation.Syntax.SyntaxTree == invocation.SyntaxTree
                    && operation.Syntax.Span.Contains(invocation.Span)
                )
                {
                    return block;
                }
            }

            if (
                block.BranchValue != null
                && block.BranchValue.Syntax.SyntaxTree == invocation.SyntaxTree
                && block.BranchValue.Syntax.Span.Contains(invocation.Span)
            )
            {
                return block;
            }
        }

        return null;
    }

    private static bool CanReachAvoiding(BasicBlock start, BasicBlock target, BasicBlock excluded)
    {
        if (ReferenceEquals(start, excluded))
            return false;

        var pending = new Queue<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (
                !visited.Add(current.Ordinal)
                || !current.IsReachable
                || ReferenceEquals(current, excluded)
            )
                continue;

            if (ReferenceEquals(current, target))
                return true;

            var fallThrough = current.FallThroughSuccessor?.Destination;
            var conditional = current.ConditionalSuccessor?.Destination;
            if (
                current.BranchValue?.ConstantValue is { HasValue: true, Value: bool branchValue }
                && current.ConditionKind != ControlFlowConditionKind.None
            )
            {
                var takesConditional =
                    current.ConditionKind == ControlFlowConditionKind.WhenTrue
                        ? branchValue
                        : !branchValue;
                if (takesConditional)
                {
                    if (conditional != null)
                        pending.Enqueue(conditional);
                }
                else if (fallThrough != null)
                {
                    pending.Enqueue(fallThrough);
                }

                continue;
            }

            if (fallThrough != null)
                pending.Enqueue(fallThrough);

            if (conditional != null)
                pending.Enqueue(conditional);
        }

        return false;
    }

    private static bool ProvesConcurrencyEnabled(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name == "IsRowVersion")
            return invocation.Arguments.IsEmpty;

        if (invocation.TargetMethod.Name != "IsConcurrencyToken")
            return false;

        if (invocation.Arguments.IsEmpty)
            return true;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType != SpecialType.System_Boolean)
                continue;

            return argument.Value.ConstantValue is { HasValue: true, Value: true };
        }

        return false;
    }

    private static bool TryGetStoreGeneratedState(
        IInvocationOperation invocation,
        out bool storeGenerated
    )
    {
        var method = invocation.TargetMethod;
        switch (method.Name)
        {
            case "HasComputedColumnSql" when IsEfHasComputedColumnSqlMethod(method):
            case "ValueGeneratedOnAddOrUpdate"
            or "ValueGeneratedOnUpdate"
                when invocation.Arguments.IsEmpty && IsEfPropertyBuilderMethod(method):
                storeGenerated = true;
                return true;
            case "ValueGeneratedNever"
            or "ValueGeneratedOnAdd"
                when invocation.Arguments.IsEmpty && IsEfPropertyBuilderMethod(method):
                storeGenerated = false;
                return true;
            default:
                storeGenerated = false;
                return false;
        }
    }

    private static bool IsEfHasComputedColumnSqlMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.Name == "HasComputedColumnSql"
            && definition.IsExtensionMethod
            && definition.ContainingType.Name == "RelationalPropertyBuilderExtensions"
            && definition.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore"
            && definition.Parameters.Length > 0
            && IsEfPropertyBuilderType(definition.Parameters[0].Type);
    }

    private static bool IsEfPropertyBuilderMethod(IMethodSymbol method)
    {
        return IsEfPropertyBuilderType(method.ContainingType);
    }

    private static bool IsEfPropertyBuilderType(ITypeSymbol? type)
    {
        return type?.OriginalDefinition.Name == "PropertyBuilder"
            && type.ContainingNamespace?.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static bool IsEfCoreBuilderMethod(IMethodSymbol method)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString();
        return namespaceName == "Microsoft.EntityFrameworkCore.Metadata.Builders"
            || namespaceName?.StartsWith(
                "Microsoft.EntityFrameworkCore.Metadata.Builders.",
                StringComparison.Ordinal
            ) == true
            || IsEfHasComputedColumnSqlMethod(method);
    }

    private static bool IsRealEfAlternateKeyMethod(IMethodSymbol method)
    {
        return method.Name == "HasAlternateKey"
            && method.ContainingType
                is {
                    Name: "EntityTypeBuilder",
                    Arity: 1,
                    ContainingNamespace: { } containingNamespace,
                }
            && containingNamespace.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Metadata.Builders"
            && method.ReturnType
                is INamedTypeSymbol
                {
                    Name: "KeyBuilder",
                    Arity: 1,
                    ContainingNamespace: { } returnNamespace,
                }
            && returnNamespace.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static bool IsModelBuilderEntityMethod(IMethodSymbol method)
    {
        return method.Name == "Entity"
            && method.ContainingType?.Name == "ModelBuilder"
            && method.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    private static void CollectConfiguredKeyProperties(
        IOperation operation,
        INamedTypeSymbol entityType,
        ImmutableHashSet<IPropertySymbol>.Builder properties
    )
    {
        if (
            operation.ConstantValue is { HasValue: true, Value: string propertyName }
            && TryFindPropertyOnEntityHierarchy(entityType, propertyName, out var namedProperty)
        )
        {
            properties.Add(namedProperty.OriginalDefinition);
        }

        if (
            operation is IPropertyReferenceOperation reference
            && IsPropertyDeclaredOnEntityHierarchy(reference.Property, entityType)
        )
        {
            properties.Add(reference.Property.OriginalDefinition);
        }

        foreach (var child in operation.ChildOperations)
            CollectConfiguredKeyProperties(child, entityType, properties);
    }

    private static bool TryFindPropertyOnEntityHierarchy(
        INamedTypeSymbol entityType,
        string propertyName,
        out IPropertySymbol property
    )
    {
        for (var current = entityType; current != null; current = current.BaseType)
        {
            property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault()!;
            if (property != null)
                return true;
        }

        property = null!;
        return false;
    }

    private static bool IsPropertyDeclaredOnEntityHierarchy(
        IPropertySymbol property,
        INamedTypeSymbol entityType
    )
    {
        for (var current = entityType; current != null; current = current.BaseType)
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    property.ContainingType.OriginalDefinition
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBoundToModelBuilderParameter(
        IInvocationOperation invocation,
        IParameterSymbol modelBuilderParameter,
        SemanticModel model,
        CancellationToken cancellationToken
    )
    {
        return invocation.Instance != null
            && IsStableModelBuilderReference(
                invocation.Instance,
                modelBuilderParameter,
                model,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                invocation.Syntax.SpanStart,
                cancellationToken
            );
    }

    private static bool IsStableModelBuilderReference(
        IOperation operation,
        IParameterSymbol modelBuilderParameter,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation is IParameterReferenceOperation parameterReference
            && SymbolEqualityComparer.Default.Equals(
                parameterReference.Parameter,
                modelBuilderParameter
            )
        )
        {
            return !HasSymbolWrite(
                modelBuilderParameter,
                operation.Syntax,
                model,
                boundary,
                cancellationToken
            );
        }

        if (operation is not ILocalReferenceOperation localReference)
            return false;

        var local = localReference.Local;
        if (!aliases.Add(local) || local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarationSyntax = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        if (
            declarationSyntax.SyntaxTree != model.SyntaxTree
            || declarationSyntax.SpanStart >= boundary
            || model.GetOperation(declarationSyntax, cancellationToken)
                is not IVariableDeclaratorOperation { Initializer.Value: { } initializer }
            || HasSymbolWrite(local, operation.Syntax, model, boundary: null, cancellationToken)
        )
        {
            return false;
        }

        return IsStableModelBuilderReference(
            initializer,
            modelBuilderParameter,
            model,
            aliases,
            declarationSyntax.SpanStart,
            cancellationToken
        );
    }

    private static bool HasSymbolWrite(
        ISymbol symbol,
        SyntaxNode referenceSyntax,
        SemanticModel model,
        int? boundary,
        CancellationToken cancellationToken
    )
    {
        var method = referenceSyntax
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        SyntaxNode? body = method?.Body ?? (SyntaxNode?)method?.ExpressionBody?.Expression;
        if (body == null || model.GetOperation(body, cancellationToken) is not { } bodyOperation)
            return true;

        var collector = new OperationCollector();
        collector.Visit(bodyOperation);
        return collector.SimpleAssignments.Any(assignment =>
                IsBeforeBoundary(assignment, boundary)
                && IsSymbolReference(assignment.Target, symbol)
            )
            || collector.CompoundAssignments.Any(assignment =>
                IsBeforeBoundary(assignment, boundary)
                && IsSymbolReference(assignment.Target, symbol)
            )
            || collector.CoalesceAssignments.Any(assignment =>
                IsBeforeBoundary(assignment, boundary)
                && IsSymbolReference(assignment.Target, symbol)
            )
            || collector.Increments.Any(increment =>
                IsBeforeBoundary(increment, boundary) && IsSymbolReference(increment.Target, symbol)
            )
            || collector.Invocations.Any(candidate =>
                IsBeforeBoundary(candidate, boundary)
                && candidate.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && IsSymbolReference(argument.Value, symbol)
                )
            );
    }

    private static bool IsBeforeBoundary(IOperation operation, int? boundary)
    {
        return boundary == null || operation.Syntax.SpanStart < boundary.Value;
    }

    private static bool IsSymbolReference(IOperation operation, ISymbol symbol)
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        return operation switch
        {
            ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(
                local.Local,
                symbol
            ),
            IParameterReferenceOperation parameter => SymbolEqualityComparer.Default.Equals(
                parameter.Parameter,
                symbol
            ),
            _ => false,
        };
    }

    private static bool TryFindConfiguredProperty(
        IOperation? operation,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken,
        out IPropertySymbol property
    )
    {
        if (
            TryGetStableLocalInitializer(
                operation,
                model,
                aliases,
                boundary,
                cancellationToken,
                out var initializer,
                out var declarationBoundary
            )
        )
        {
            return TryFindConfiguredProperty(
                initializer,
                model,
                aliases,
                declarationBoundary,
                cancellationToken,
                out property
            );
        }

        operation = operation == null ? null : LostUpdateOperationFacts.Unwrap(operation);
        if (operation is IInvocationOperation invocation)
        {
            if (
                invocation.TargetMethod.Name == "Property"
                && IsEfCoreBuilderMethod(invocation.TargetMethod)
            )
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (FindPropertyReference(argument.Value) is { } configuredProperty)
                    {
                        property = configuredProperty;
                        return true;
                    }
                }
            }

            if (IsEfCoreBuilderMethod(invocation.TargetMethod))
            {
                return TryFindConfiguredProperty(
                    GetInvocationReceiver(invocation),
                    model,
                    aliases,
                    invocation.Syntax.SpanStart,
                    cancellationToken,
                    out property
                );
            }
        }

        property = null!;
        return false;
    }

    private static bool TryFindConfiguredPropertyName(
        IOperation? operation,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken,
        out string propertyName
    )
    {
        if (
            TryGetStableLocalInitializer(
                operation,
                model,
                aliases,
                boundary,
                cancellationToken,
                out var initializer,
                out var declarationBoundary
            )
        )
        {
            return TryFindConfiguredPropertyName(
                initializer,
                model,
                aliases,
                declarationBoundary,
                cancellationToken,
                out propertyName
            );
        }

        operation = operation == null ? null : LostUpdateOperationFacts.Unwrap(operation);
        if (operation is IInvocationOperation invocation)
        {
            if (
                invocation.TargetMethod.Name == "Property"
                && IsEfCoreBuilderMethod(invocation.TargetMethod)
            )
            {
                foreach (var argument in invocation.Arguments)
                {
                    if (
                        argument.Parameter?.Type.SpecialType == SpecialType.System_String
                        && argument.Value.ConstantValue
                            is { HasValue: true, Value: string configuredName }
                        && !string.IsNullOrWhiteSpace(configuredName)
                    )
                    {
                        propertyName = configuredName;
                        return true;
                    }
                }
            }

            if (IsEfCoreBuilderMethod(invocation.TargetMethod))
            {
                return TryFindConfiguredPropertyName(
                    GetInvocationReceiver(invocation),
                    model,
                    aliases,
                    invocation.Syntax.SpanStart,
                    cancellationToken,
                    out propertyName
                );
            }
        }

        propertyName = string.Empty;
        return false;
    }

    private static bool TryFindConfiguredEntity(
        IOperation? operation,
        IParameterSymbol modelBuilderParameter,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken,
        out INamedTypeSymbol entityType
    )
    {
        if (
            TryGetStableLocalInitializer(
                operation,
                model,
                aliases,
                boundary,
                cancellationToken,
                out var initializer,
                out var declarationBoundary
            )
        )
        {
            return TryFindConfiguredEntity(
                initializer,
                modelBuilderParameter,
                model,
                aliases,
                declarationBoundary,
                cancellationToken,
                out entityType
            );
        }

        operation = operation == null ? null : LostUpdateOperationFacts.Unwrap(operation);
        if (operation is IInvocationOperation invocation)
        {
            if (
                invocation.TargetMethod.TypeArguments.Length == 1
                && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol configuredType
                && IsModelBuilderEntityMethod(invocation.TargetMethod)
                && IsBoundToModelBuilderParameter(
                    invocation,
                    modelBuilderParameter,
                    model,
                    cancellationToken
                )
            )
            {
                entityType = configuredType;
                return true;
            }

            if (IsEfCoreBuilderMethod(invocation.TargetMethod))
            {
                return TryFindConfiguredEntity(
                    GetInvocationReceiver(invocation),
                    modelBuilderParameter,
                    model,
                    aliases,
                    invocation.Syntax.SpanStart,
                    cancellationToken,
                    out entityType
                );
            }
        }

        entityType = null!;
        return false;
    }

    private static bool IsStableEfBuilderReference(
        IOperation operation,
        IParameterSymbol builderParameter,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken
    )
    {
        operation = LostUpdateOperationFacts.Unwrap(operation);
        if (
            operation is IParameterReferenceOperation parameterReference
            && SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, builderParameter)
        )
        {
            return !HasSymbolWrite(
                builderParameter,
                operation.Syntax,
                model,
                boundary: null,
                cancellationToken
            );
        }

        if (
            TryGetStableLocalInitializer(
                operation,
                model,
                aliases,
                boundary,
                cancellationToken,
                out var initializer,
                out var declarationBoundary
            )
        )
        {
            return IsStableEfBuilderReference(
                initializer,
                builderParameter,
                model,
                aliases,
                declarationBoundary,
                cancellationToken
            );
        }

        return operation is IInvocationOperation invocation
            && IsEfCoreBuilderMethod(invocation.TargetMethod)
            && GetInvocationReceiver(invocation) is { } receiver
            && IsStableEfBuilderReference(
                receiver,
                builderParameter,
                model,
                aliases,
                invocation.Syntax.SpanStart,
                cancellationToken
            );
    }

    private static bool TryGetStableLocalInitializer(
        IOperation? operation,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases,
        int boundary,
        CancellationToken cancellationToken,
        out IOperation initializer,
        out int declarationBoundary
    )
    {
        operation = operation == null ? null : LostUpdateOperationFacts.Unwrap(operation);
        if (operation is not ILocalReferenceOperation localReference)
        {
            initializer = null!;
            declarationBoundary = 0;
            return false;
        }

        var local = localReference.Local;
        if (!aliases.Add(local) || local.DeclaringSyntaxReferences.Length != 1)
        {
            initializer = null!;
            declarationBoundary = 0;
            return false;
        }

        var declarationSyntax = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        if (
            declarationSyntax.SyntaxTree != model.SyntaxTree
            || declarationSyntax.SpanStart >= boundary
            || model.GetOperation(declarationSyntax, cancellationToken)
                is not IVariableDeclaratorOperation { Initializer.Value: { } value }
            || HasSymbolWrite(local, operation.Syntax, model, boundary: null, cancellationToken)
        )
        {
            initializer = null!;
            declarationBoundary = 0;
            return false;
        }

        initializer = value;
        declarationBoundary = declarationSyntax.SpanStart;
        return true;
    }

    private static IOperation? GetInvocationReceiver(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance;

        if (
            invocation.Syntax
                is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax memberAccess,
                }
            && invocation.SemanticModel?.GetOperation(memberAccess.Expression) is { } receiver
        )
        {
            return receiver;
        }

        return
            (
                invocation.TargetMethod.IsExtensionMethod
                || invocation.TargetMethod.ReducedFrom != null
            )
            && invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value
            : null;
    }

    private static bool TryFindConfiguredEntity(
        IInvocationOperation invocation,
        IParameterSymbol modelBuilderParameter,
        SemanticModel model,
        CancellationToken cancellationToken,
        out INamedTypeSymbol entityType
    )
    {
        return TryFindConfiguredEntity(
            GetInvocationReceiver(invocation),
            modelBuilderParameter,
            model,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            invocation.Syntax.SpanStart,
            cancellationToken,
            out entityType
        );
    }

    private readonly struct ContextTrackingModel
    {
        internal ContextTrackingModel(
            bool defaultNoTracking,
            bool independentChangeDetection,
            ImmutableArray<NotificationTrackingEvent> notificationStrategies
        )
        {
            DefaultNoTracking = defaultNoTracking;
            IndependentChangeDetection = independentChangeDetection;
            NotificationStrategies = notificationStrategies;
        }

        internal bool DefaultNoTracking { get; }
        internal bool IndependentChangeDetection { get; }
        internal ImmutableArray<NotificationTrackingEvent> NotificationStrategies { get; }
    }

    private enum ContextTrackingHook
    {
        Configuring,
        ModelCreating,
    }

    private readonly struct ContextTrackingState
    {
        internal ContextTrackingState(
            bool hasQueryEvent,
            bool? defaultNoTracking,
            bool hasProxyEvent,
            bool? changeTrackingProxies,
            ImmutableArray<NotificationTrackingEvent> notificationStrategies
        )
        {
            HasQueryEvent = hasQueryEvent;
            DefaultNoTracking = defaultNoTracking;
            HasProxyEvent = hasProxyEvent;
            ChangeTrackingProxies = changeTrackingProxies;
            NotificationStrategies = notificationStrategies;
        }

        internal bool HasQueryEvent { get; }
        internal bool? DefaultNoTracking { get; }
        internal bool HasProxyEvent { get; }
        internal bool? ChangeTrackingProxies { get; }
        internal ImmutableArray<NotificationTrackingEvent> NotificationStrategies { get; }
    }

    private readonly struct NotificationTrackingEvent
    {
        internal NotificationTrackingEvent(INamedTypeSymbol? entityType, bool? value)
        {
            EntityType = entityType;
            Value = value;
        }

        internal INamedTypeSymbol? EntityType { get; }
        internal bool? Value { get; }

        internal NotificationTrackingEvent WithAmbiguousValue() => new(EntityType, value: null);
    }

    private readonly struct ContextTrackingEvent
    {
        private ContextTrackingEvent(
            int position,
            ContextTrackingHook hook,
            INamedTypeSymbol? baseContextType,
            INamedTypeSymbol? entityType,
            bool isConditionalBaseCall,
            bool? defaultNoTracking,
            bool? changeTrackingProxies,
            bool? notificationStrategy,
            bool isQueryEvent,
            bool isProxyEvent,
            bool isModelEvent,
            bool isRegistration
        )
        {
            Position = position;
            Hook = hook;
            BaseContextType = baseContextType;
            EntityType = entityType;
            IsConditionalBaseCall = isConditionalBaseCall;
            DefaultNoTracking = defaultNoTracking;
            ChangeTrackingProxies = changeTrackingProxies;
            NotificationStrategy = notificationStrategy;
            IsQueryEvent = isQueryEvent;
            IsProxyEvent = isProxyEvent;
            IsModelEvent = isModelEvent;
            IsRegistration = isRegistration;
        }

        internal int Position { get; }
        internal ContextTrackingHook Hook { get; }
        internal INamedTypeSymbol? BaseContextType { get; }
        internal INamedTypeSymbol? EntityType { get; }
        internal bool IsConditionalBaseCall { get; }
        internal bool? DefaultNoTracking { get; }
        internal bool? ChangeTrackingProxies { get; }
        internal bool? NotificationStrategy { get; }
        internal bool IsQueryEvent { get; }
        internal bool IsProxyEvent { get; }
        internal bool IsModelEvent { get; }
        internal bool IsRegistration { get; }

        internal static ContextTrackingEvent ForOptions(
            int position,
            bool? noTracking,
            bool? proxies,
            bool isQueryEvent,
            bool isRegistration
        ) =>
            new(
                position,
                ContextTrackingHook.Configuring,
                baseContextType: null,
                entityType: null,
                isConditionalBaseCall: false,
                noTracking,
                proxies,
                notificationStrategy: null,
                isQueryEvent,
                isProxyEvent: !isQueryEvent,
                isModelEvent: false,
                isRegistration
            );

        internal static ContextTrackingEvent ForModel(
            int position,
            INamedTypeSymbol? entityType,
            bool? notificationStrategy
        ) =>
            new(
                position,
                ContextTrackingHook.ModelCreating,
                baseContextType: null,
                entityType,
                isConditionalBaseCall: false,
                defaultNoTracking: null,
                changeTrackingProxies: null,
                notificationStrategy,
                isQueryEvent: false,
                isProxyEvent: false,
                isModelEvent: true,
                isRegistration: false
            );

        internal static ContextTrackingEvent ForBaseCall(
            int position,
            INamedTypeSymbol baseContextType,
            ContextTrackingHook hook,
            bool isConditional
        ) =>
            new(
                position,
                hook,
                baseContextType,
                entityType: null,
                isConditional,
                defaultNoTracking: null,
                changeTrackingProxies: null,
                notificationStrategy: null,
                isQueryEvent: false,
                isProxyEvent: false,
                isModelEvent: false,
                isRegistration: false
            );
    }

    private sealed class FluentModelEvent
    {
        private FluentModelEvent(
            int position,
            INamedTypeSymbol? baseContextType,
            INamedTypeSymbol? entityType,
            IPropertySymbol? property,
            string? propertyName,
            bool enabled,
            bool? rowVersion,
            bool? keyless,
            IPropertySymbol? ignoredProperty,
            string? ignoredPropertyName,
            IPropertySymbol? mappedProperty,
            string? mappedPropertyName,
            bool mappedPropertyIsConditional = false,
            ImmutableHashSet<IPropertySymbol>? primaryKeys = null,
            IPropertySymbol? storeGeneratedProperty = null,
            string? storeGeneratedPropertyName = null,
            bool? storeGenerated = null,
            ImmutableHashSet<IPropertySymbol>? alternateKeys = null
        )
        {
            Position = position;
            BaseContextType = baseContextType;
            EntityType = entityType;
            Property = property;
            PropertyName = propertyName;
            Enabled = enabled;
            RowVersion = rowVersion;
            Keyless = keyless;
            IgnoredProperty = ignoredProperty;
            IgnoredPropertyName = ignoredPropertyName;
            MappedProperty = mappedProperty;
            MappedPropertyName = mappedPropertyName;
            MappedPropertyIsConditional = mappedPropertyIsConditional;
            PrimaryKeys = primaryKeys;
            StoreGeneratedProperty = storeGeneratedProperty;
            StoreGeneratedPropertyName = storeGeneratedPropertyName;
            StoreGenerated = storeGenerated;
            AlternateKeys = alternateKeys;
        }

        internal int Position { get; }
        internal INamedTypeSymbol? BaseContextType { get; }
        internal INamedTypeSymbol? EntityType { get; }
        internal IPropertySymbol? Property { get; }
        internal string? PropertyName { get; }
        internal bool Enabled { get; }
        internal bool? RowVersion { get; }
        internal bool? Keyless { get; }
        internal IPropertySymbol? IgnoredProperty { get; }
        internal string? IgnoredPropertyName { get; }
        internal IPropertySymbol? MappedProperty { get; }
        internal string? MappedPropertyName { get; }
        internal bool MappedPropertyIsConditional { get; }
        internal ImmutableHashSet<IPropertySymbol>? PrimaryKeys { get; }
        internal IPropertySymbol? StoreGeneratedProperty { get; }
        internal string? StoreGeneratedPropertyName { get; }
        internal bool? StoreGenerated { get; }
        internal ImmutableHashSet<IPropertySymbol>? AlternateKeys { get; }

        internal static FluentModelEvent CreateBaseCall(
            int position,
            INamedTypeSymbol baseContextType
        )
        {
            return new FluentModelEvent(
                position,
                baseContextType,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null
            );
        }

        internal static FluentModelEvent CreatePropertyState(
            int position,
            INamedTypeSymbol entityType,
            IPropertySymbol? property,
            string? propertyName,
            bool enabled,
            bool? rowVersion
        )
        {
            return new FluentModelEvent(
                position,
                baseContextType: null,
                entityType,
                property,
                propertyName,
                enabled,
                rowVersion,
                keyless: null,
                ignoredProperty: null,
                ignoredPropertyName: null,
                mappedProperty: null,
                mappedPropertyName: null,
                storeGeneratedProperty: rowVersion == true ? property : null,
                storeGeneratedPropertyName: rowVersion == true ? propertyName : null,
                storeGenerated: rowVersion == true ? true : null
            );
        }

        internal static FluentModelEvent CreateStoreGeneratedState(
            int position,
            INamedTypeSymbol entityType,
            IPropertySymbol? property,
            string? propertyName,
            bool storeGenerated
        )
        {
            return new FluentModelEvent(
                position,
                baseContextType: null,
                entityType,
                property: null,
                propertyName: null,
                enabled: false,
                rowVersion: null,
                keyless: null,
                ignoredProperty: null,
                ignoredPropertyName: null,
                mappedProperty: null,
                mappedPropertyName: null,
                storeGeneratedProperty: property,
                storeGeneratedPropertyName: propertyName,
                storeGenerated: storeGenerated
            );
        }

        internal static FluentModelEvent CreateKeylessState(
            int position,
            INamedTypeSymbol entityType,
            bool keyless,
            ImmutableHashSet<IPropertySymbol>? primaryKeys
        )
        {
            return new FluentModelEvent(
                position,
                null,
                entityType,
                null,
                null,
                false,
                null,
                keyless,
                null,
                null,
                null,
                null,
                false,
                primaryKeys
            );
        }

        internal static FluentModelEvent CreateAlternateKey(
            int position,
            INamedTypeSymbol entityType,
            ImmutableHashSet<IPropertySymbol> alternateKeys
        )
        {
            return new FluentModelEvent(
                position,
                baseContextType: null,
                entityType,
                property: null,
                propertyName: null,
                enabled: false,
                rowVersion: null,
                keyless: null,
                ignoredProperty: null,
                ignoredPropertyName: null,
                mappedProperty: null,
                mappedPropertyName: null,
                alternateKeys: alternateKeys
            );
        }

        internal static FluentModelEvent CreateIgnoredProperty(
            int position,
            INamedTypeSymbol entityType,
            IPropertySymbol? ignoredProperty,
            string? ignoredPropertyName
        )
        {
            return new FluentModelEvent(
                position,
                null,
                entityType,
                null,
                null,
                false,
                null,
                null,
                ignoredProperty,
                ignoredPropertyName,
                null,
                null
            );
        }

        internal static FluentModelEvent CreateMappedProperty(
            int position,
            INamedTypeSymbol entityType,
            IPropertySymbol? mappedProperty,
            string? mappedPropertyName,
            bool mappedPropertyIsConditional
        )
        {
            return new FluentModelEvent(
                position,
                null,
                entityType,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                mappedProperty,
                mappedPropertyName,
                mappedPropertyIsConditional
            );
        }
    }
}

internal sealed class HelperSummary
{
    private HelperSummary(
        ImmutableArray<HelperMutation> mutations,
        ImmutableArray<HelperSave> saveEffects,
        ImmutableArray<HelperTransaction> transactionEffects
    )
    {
        Mutations = mutations;
        SaveEffects = saveEffects;
        TransactionEffects = transactionEffects;
    }

    internal ImmutableArray<HelperMutation> Mutations { get; }
    internal ImmutableArray<HelperSave> SaveEffects { get; }
    internal ImmutableArray<HelperTransaction> TransactionEffects { get; }
    internal bool IsEmpty => Mutations.IsEmpty && SaveEffects.IsEmpty && TransactionEffects.IsEmpty;

    internal static HelperSummary Create(
        IMethodSymbol method,
        IOperation body,
        ControlFlowGraph? flowGraph = null
    )
    {
        var collector = new OperationCollector();
        collector.Visit(body);
        flowGraph ??= TryCreateFlowGraph(body);
        if (
            collector.HasUnsupportedHelperControlFlow
            || collector.HasConditionalControlFlow
                && flowGraph == null
                && method.MethodKind != MethodKind.LocalFunction
            || method.MethodKind == MethodKind.LocalFunction
                && ContainsCapturedCondition(body, method)
            || collector.SimpleAssignments.Any(assignment =>
                LostUpdateOperationFacts.Unwrap(assignment.Target)
                    is IParameterReferenceOperation parameterReference
                && SymbolEqualityComparer.Default.Equals(
                    parameterReference.Parameter.ContainingSymbol,
                    method
                )
            )
            || collector.Invocations.Any(invocation =>
                invocation.Arguments.Any(argument =>
                    argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                    && LostUpdateOperationFacts.TryGetRootParameter(
                        argument.Value,
                        out var reboundParameter
                    )
                    && SymbolEqualityComparer.Default.Equals(
                        reboundParameter.ContainingSymbol,
                        method
                    )
                )
            )
            || body.Syntax.DescendantNodesAndSelf()
                .Any(node =>
                    node
                        is CommonForEachStatementSyntax
                            or ForStatementSyntax
                            or WhileStatementSyntax
                            or DoStatementSyntax
                            or SwitchExpressionSyntax
                            or ThrowStatementSyntax
                            or ThrowExpressionSyntax
                )
        )
        {
            return new HelperSummary(
                ImmutableArray<HelperMutation>.Empty,
                ImmutableArray<HelperSave>.Empty,
                ImmutableArray<HelperTransaction>.Empty
            );
        }
        var mutations = ImmutableArray.CreateBuilder<HelperMutation>();
        var saves = new List<HelperSave>();
        var transactions = new List<HelperTransaction>();
        var mutationOperations = new Dictionary<int, IOperation>();
        var saveOperations = new Dictionary<int, IOperation>();
        var transactionOperations = new Dictionary<int, IOperation>();

        foreach (
            var invocation in collector.Invocations.Where(candidate =>
                IsReachable(candidate, flowGraph)
            )
        )
        {
            if (
                LostUpdateOperationFacts.IsTransactionOperation(invocation)
                && TryGetTransactionTarget(invocation, method, out var transactionTarget)
                && TryCreateHelperTransaction(
                    invocation,
                    transactionTarget,
                    method,
                    flowGraph,
                    out var helperTransaction
                )
            )
            {
                transactions.Add(helperTransaction);
                transactionOperations[invocation.Syntax.SpanStart] = invocation;
            }

            if (
                LostUpdateOperationFacts.IsSaveChanges(invocation)
                && TryGetHelperTarget(invocation.Instance, method, out var saveTarget)
                && TryCreateHelperSave(
                    invocation,
                    saveTarget,
                    method,
                    flowGraph,
                    out var helperSave
                )
            )
            {
                saves.Add(helperSave);
                saveOperations[invocation.Syntax.SpanStart] = invocation;
            }
        }

        foreach (
            var compound in collector.CompoundAssignments.Where(candidate =>
                IsReachable(candidate, flowGraph)
            )
        )
            AddMutation(compound.Target, compound, method, mutations, mutationOperations);

        foreach (
            var coalesce in collector.CoalesceAssignments.Where(candidate =>
                IsReachable(candidate, flowGraph)
            )
        )
            AddMutation(coalesce.Target, coalesce, method, mutations, mutationOperations);

        foreach (
            var increment in collector.Increments.Where(candidate =>
                IsReachable(candidate, flowGraph)
            )
        )
            AddMutation(increment.Target, increment, method, mutations, mutationOperations);

        foreach (var assignment in collector.SimpleAssignments)
        {
            if (
                !IsReachable(assignment, flowGraph)
                || assignment.Target is not IPropertyReferenceOperation target
                || !TryGetHelperTarget(target.Instance, method, out var helperTarget)
            )
            {
                continue;
            }

            if (
                LostUpdateOperationFacts.ContainsPropertyRead(
                    assignment.Value,
                    target.Property,
                    helperTarget.Symbol
                )
                || LostUpdateOperationFacts.IsGuardedByPropertyRead(
                    assignment,
                    target.Property,
                    helperTarget.Symbol
                )
            )
            {
                mutations.Add(
                    new HelperMutation(
                        helperTarget,
                        target.Property,
                        target.Syntax.GetLocation(),
                        assignment.Syntax.SpanStart
                    )
                );
                mutationOperations[assignment.Syntax.SpanStart] = assignment;
            }
        }

        var retainedMutations = ImmutableArray.CreateBuilder<HelperMutation>();
        foreach (var mutation in mutations)
        {
            var subsequentSaves = saves
                .Where(save =>
                    CanFlowToSave(
                        mutationOperations[mutation.Position],
                        saveOperations[save.Position],
                        flowGraph
                    )
                )
                .ToList();
            if (
                IsDefinitelyOverwritten(
                    mutation,
                    mutationOperations[mutation.Position],
                    collector.SimpleAssignments.Where(assignment =>
                        IsReachable(assignment, flowGraph)
                    ),
                    method,
                    subsequentSaves.Select(save => saveOperations[save.Position]),
                    flowGraph
                )
            )
            {
                continue;
            }

            var retainedMutation = TryGetExactHelperEffectCondition(
                mutationOperations[mutation.Position],
                method,
                flowGraph,
                out var conditionParameterOrdinal,
                out var conditionValue
            )
                ? mutation.WithCondition(conditionParameterOrdinal, conditionValue)
                : mutation;
            retainedMutations.Add(
                retainedMutation.WithSubsequentSaves(subsequentSaves.ToImmutableArray())
            );
        }

        var retainedTransactions = transactions
            .Select(transaction =>
                transaction.WithProtectedSavePositions(
                    saves
                        .Where(save =>
                            TransactionDominatesSave(
                                transaction,
                                transactionOperations[transaction.Position],
                                saveOperations[save.Position],
                                method,
                                flowGraph
                            )
                            && HelperTransactionLifetimeCoversSave(
                                transactionOperations[transaction.Position],
                                saveOperations[save.Position],
                                collector
                            )
                        )
                        .Select(save => save.Position)
                        .ToImmutableArray()
                )
            )
            .Where(transaction => !transaction.ProtectedSavePositions.IsEmpty)
            .ToImmutableArray();

        return new HelperSummary(
            retainedMutations.ToImmutable(),
            saves.ToImmutableArray(),
            retainedTransactions
        );
    }

    private static bool ContainsCapturedCondition(IOperation operation, IMethodSymbol method)
    {
        if (
            operation is IConditionalOperation conditional
            && ContainsCapturedReference(conditional.Condition, method)
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is IAnonymousFunctionOperation or ILocalFunctionOperation)
                continue;
            if (ContainsCapturedCondition(child, method))
                return true;
        }

        return false;
    }

    private static bool ContainsCapturedReference(IOperation operation, IMethodSymbol method)
    {
        if (
            operation is ILocalReferenceOperation
            || operation is IParameterReferenceOperation parameter
                && (
                    parameter.Parameter.ContainingSymbol is not IMethodSymbol containingMethod
                    || !SymbolEqualityComparer.Default.Equals(
                        containingMethod.OriginalDefinition,
                        method.OriginalDefinition
                    )
                )
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsCapturedReference(child, method))
                return true;
        }

        return false;
    }

    private static bool TryGetTransactionTarget(
        IOperation operation,
        IMethodSymbol method,
        out HelperTarget target
    )
    {
        if (
            TryGetHelperTarget(operation, method, out target)
            && LostUpdateOperationFacts.IsDbContextType(target.Type)
        )
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (TryGetTransactionTarget(child, method, out target))
                return true;
        }

        target = default;
        return false;
    }

    private static bool TryGetHelperTarget(
        IOperation? operation,
        IMethodSymbol method,
        out HelperTarget target
    )
    {
        operation = operation == null ? null : LostUpdateOperationFacts.Unwrap(operation);
        ISymbol? symbol = operation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            _ => null,
        };
        if (symbol == null)
        {
            target = default;
            return false;
        }

        if (
            symbol is IParameterSymbol directParameter
            && SymbolEqualityComparer.Default.Equals(directParameter.ContainingSymbol, method)
        )
        {
            target = new HelperTarget(directParameter);
            return true;
        }

        if (method.MethodKind == MethodKind.LocalFunction)
        {
            target = new HelperTarget(symbol);
            return true;
        }

        target = default;
        return false;
    }

    private static ControlFlowGraph? TryCreateFlowGraph(IOperation body)
    {
        try
        {
            return body switch
            {
                IMethodBodyOperation methodBody when methodBody.Parent == null =>
                    ControlFlowGraph.Create(methodBody),
                IBlockOperation block when block.Parent == null => ControlFlowGraph.Create(block),
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

    private static bool IsReachable(IOperation operation, ControlFlowGraph? flowGraph)
    {
        if (flowGraph == null)
            return true;

        var block = FindContainingBlock(flowGraph, operation);
        return block != null && CanReach(flowGraph.Blocks[0], block);
    }

    private static bool TryCreateHelperTransaction(
        IInvocationOperation invocation,
        HelperTarget transactionTarget,
        IMethodSymbol method,
        ControlFlowGraph? flowGraph,
        out HelperTransaction transaction
    )
    {
        if (flowGraph == null)
        {
            if (
                TryGetExactHelperEffectCondition(
                    invocation,
                    method,
                    null,
                    out var directConditionParameterOrdinal,
                    out var directConditionValue
                )
            )
            {
                transaction = new HelperTransaction(
                    transactionTarget,
                    invocation.Syntax.SpanStart,
                    conditionParameterOrdinal: directConditionParameterOrdinal,
                    conditionValue: directConditionValue
                );
            }
            else
            {
                transaction = new HelperTransaction(transactionTarget, invocation.Syntax.SpanStart);
            }
            return true;
        }

        var transactionBlock = FindContainingBlock(flowGraph, invocation);
        var entryBlock = flowGraph.Blocks[0];
        var exitBlock = flowGraph.Blocks[flowGraph.Blocks.Length - 1];
        if (transactionBlock == null || !CanReach(entryBlock, transactionBlock))
        {
            transaction = default;
            return false;
        }

        if (!CanReachAvoiding(entryBlock, exitBlock, transactionBlock, method, null, false))
        {
            transaction = new HelperTransaction(transactionTarget, invocation.Syntax.SpanStart);
            return true;
        }

        if (
            TryGetExactHelperEffectCondition(
                invocation,
                method,
                flowGraph,
                out var conditionParameterOrdinal,
                out var conditionValue
            )
        )
        {
            transaction = new HelperTransaction(
                transactionTarget,
                invocation.Syntax.SpanStart,
                conditionParameterOrdinal: conditionParameterOrdinal,
                conditionValue: conditionValue
            );
            return true;
        }

        transaction = default;
        return false;
    }

    private static bool TransactionDominatesSave(
        HelperTransaction transaction,
        IOperation transactionOperation,
        IOperation saveOperation,
        IMethodSymbol method,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return transaction.Position < saveOperation.Syntax.SpanStart;

        var transactionBlock = FindContainingBlock(flowGraph, transactionOperation);
        var saveBlock = FindContainingBlock(flowGraph, saveOperation);
        if (transactionBlock == null || saveBlock == null)
            return false;
        if (ReferenceEquals(transactionBlock, saveBlock))
        {
            return transaction.Position < saveOperation.Syntax.SpanStart
                && (
                    !transaction.ConditionParameterOrdinal.HasValue
                    || !CanReach(
                        flowGraph.Blocks[0],
                        saveBlock,
                        method,
                        transaction.ConditionParameterOrdinal,
                        !transaction.ConditionValue
                    )
                );
        }

        var entryBlock = flowGraph.Blocks[0];
        if (transaction.ConditionParameterOrdinal.HasValue)
        {
            return CanReach(
                    entryBlock,
                    saveBlock,
                    method,
                    transaction.ConditionParameterOrdinal,
                    transaction.ConditionValue
                )
                && !CanReach(
                    entryBlock,
                    saveBlock,
                    method,
                    transaction.ConditionParameterOrdinal,
                    !transaction.ConditionValue
                )
                && !CanReachAvoiding(
                    entryBlock,
                    saveBlock,
                    transactionBlock,
                    method,
                    transaction.ConditionParameterOrdinal,
                    transaction.ConditionValue
                );
        }

        return CanReach(entryBlock, transactionBlock)
            && CanReach(transactionBlock, saveBlock)
            && !CanReachAvoiding(entryBlock, saveBlock, transactionBlock);
    }

    private static bool HelperTransactionLifetimeCoversSave(
        IOperation transactionOperation,
        IOperation saveOperation,
        OperationCollector collector
    )
    {
        var transactionSyntax = transactionOperation.Syntax;
        var usingStatement = transactionSyntax
            .AncestorsAndSelf()
            .OfType<UsingStatementSyntax>()
            .FirstOrDefault(candidate =>
                (
                    candidate.Expression?.Span.Contains(transactionSyntax.Span) == true
                    || candidate.Declaration?.Span.Contains(transactionSyntax.Span) == true
                ) && candidate.Statement.Span.Contains(saveOperation.Syntax.Span)
            );
        if (usingStatement != null)
            return true;

        var usingDeclaration = transactionSyntax
            .AncestorsAndSelf()
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault(candidate =>
                !candidate.UsingKeyword.IsKind(SyntaxKind.None)
                && candidate.Declaration.Span.Contains(transactionSyntax.Span)
                && candidate.Parent is BlockSyntax scope
                && scope.Span.Contains(saveOperation.Syntax.Span)
            );

        if (!TryGetHelperTransactionLocal(transactionOperation, out var local))
            return usingDeclaration != null;

        var start = transactionSyntax.SpanStart;
        var end = saveOperation.Syntax.SpanStart;
        return !collector.SimpleAssignments.Any(assignment =>
                assignment.Syntax.SpanStart > start
                && assignment.Syntax.SpanStart < end
                && LostUpdateOperationFacts.Unwrap(assignment.Target)
                    is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local)
            )
            && !collector.Invocations.Any(invocation =>
                invocation.Syntax.SpanStart > start
                && invocation.Syntax.SpanStart < end
                && (
                    invocation.Arguments.Any(argument =>
                        LostUpdateOperationFacts.Unwrap(argument.Value)
                            is ILocalReferenceOperation reference
                        && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                    )
                    || invocation.Instance != null
                        && LostUpdateOperationFacts.Unwrap(invocation.Instance)
                            is ILocalReferenceOperation reference
                        && SymbolEqualityComparer.Default.Equals(reference.Local, local)
                        && LostUpdateOperationFacts.IsTransactionTerminationMethod(
                            invocation.TargetMethod
                        )
                )
            );
    }

    private static bool TryGetHelperTransactionLocal(
        IOperation transactionOperation,
        out ILocalSymbol local
    )
    {
        IOperation current = transactionOperation;
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
                case IVariableInitializerOperation
                {
                    Parent: IVariableDeclaratorOperation declarator,
                }:
                    local = declarator.Symbol;
                    return true;
                default:
                    local = null!;
                    return false;
            }
        }

        local = null!;
        return false;
    }

    private static bool TryCreateHelperSave(
        IInvocationOperation invocation,
        HelperTarget saveTarget,
        IMethodSymbol method,
        ControlFlowGraph? flowGraph,
        out HelperSave save
    )
    {
        if (flowGraph == null)
        {
            if (
                TryGetExactHelperEffectCondition(
                    invocation,
                    method,
                    null,
                    out var directConditionParameterOrdinal,
                    out var directConditionValue
                )
            )
            {
                save = new HelperSave(
                    saveTarget,
                    invocation.Syntax.GetLocation(),
                    invocation.Syntax.SpanStart,
                    directConditionParameterOrdinal,
                    directConditionValue
                );
            }
            else
            {
                save = new HelperSave(
                    saveTarget,
                    invocation.Syntax.GetLocation(),
                    invocation.Syntax.SpanStart
                );
            }
            return true;
        }

        var saveBlock = FindContainingBlock(flowGraph, invocation);
        var entryBlock = flowGraph.Blocks[0];
        var exitBlock = flowGraph.Blocks[flowGraph.Blocks.Length - 1];
        if (saveBlock == null || !CanReach(entryBlock, saveBlock))
        {
            save = default;
            return false;
        }

        if (!CanReachAvoiding(entryBlock, exitBlock, saveBlock, method, null, false))
        {
            save = new HelperSave(
                saveTarget,
                invocation.Syntax.GetLocation(),
                invocation.Syntax.SpanStart
            );
            return true;
        }

        if (
            TryGetExactHelperEffectCondition(
                invocation,
                method,
                flowGraph,
                out var conditionParameterOrdinal,
                out var conditionValue
            )
        )
        {
            save = new HelperSave(
                saveTarget,
                invocation.Syntax.GetLocation(),
                invocation.Syntax.SpanStart,
                conditionParameterOrdinal,
                conditionValue
            );
            return true;
        }

        save = default;
        return false;
    }

    private static bool TryGetExactHelperEffectCondition(
        IOperation effect,
        IMethodSymbol method,
        ControlFlowGraph? flowGraph,
        out int conditionParameterOrdinal,
        out bool conditionValue
    )
    {
        if (flowGraph != null)
        {
            var effectBlock = FindContainingBlock(flowGraph, effect);
            if (effectBlock != null)
            {
                var entryBlock = flowGraph.Blocks[0];
                var exitBlock = flowGraph.Blocks[flowGraph.Blocks.Length - 1];
                foreach (
                    var conditionParameter in method.Parameters.Where(parameter =>
                        parameter.Type.SpecialType == SpecialType.System_Boolean
                    )
                )
                {
                    foreach (var assumedValue in new[] { false, true })
                    {
                        if (
                            CanReach(
                                entryBlock,
                                effectBlock,
                                method,
                                conditionParameter.Ordinal,
                                assumedValue
                            )
                            && !CanReachAvoiding(
                                entryBlock,
                                exitBlock,
                                effectBlock,
                                method,
                                conditionParameter.Ordinal,
                                assumedValue
                            )
                            && !CanReach(
                                entryBlock,
                                effectBlock,
                                method,
                                conditionParameter.Ordinal,
                                !assumedValue
                            )
                        )
                        {
                            conditionParameterOrdinal = conditionParameter.Ordinal;
                            conditionValue = assumedValue;
                            return true;
                        }
                    }
                }
            }
        }

        for (IOperation? current = effect; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is not IConditionalOperation conditional)
                continue;

            if (
                LostUpdateOperationFacts.Unwrap(conditional.Condition)
                    is IParameterReferenceOperation parameter
                && parameter.Parameter.Type.SpecialType == SpecialType.System_Boolean
                && parameter.Parameter.ContainingSymbol is IMethodSymbol containingMethod
                && SymbolEqualityComparer.Default.Equals(
                    containingMethod.OriginalDefinition,
                    method.OriginalDefinition
                )
            )
            {
                if (IsDescendantOf(effect, conditional.WhenTrue))
                {
                    conditionParameterOrdinal = parameter.Parameter.Ordinal;
                    conditionValue = true;
                    return true;
                }

                if (conditional.WhenFalse != null && IsDescendantOf(effect, conditional.WhenFalse))
                {
                    conditionParameterOrdinal = parameter.Parameter.Ordinal;
                    conditionValue = false;
                    return true;
                }
            }
        }

        static bool IsDescendantOf(IOperation candidate, IOperation ancestor)
        {
            for (IOperation? current = candidate; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }

            return false;
        }

        conditionParameterOrdinal = 0;
        conditionValue = false;
        return false;
    }

    private static bool CanFlowToSave(
        IOperation mutation,
        IOperation save,
        ControlFlowGraph? flowGraph
    )
    {
        if (flowGraph == null)
            return save.Syntax.SpanStart > mutation.Syntax.SpanStart;

        var mutationBlock = FindContainingBlock(flowGraph, mutation);
        var saveBlock = FindContainingBlock(flowGraph, save);
        if (
            mutationBlock == null
            || saveBlock == null
            || !CanReach(flowGraph.Blocks[0], mutationBlock)
            || !CanReach(flowGraph.Blocks[0], saveBlock)
            || ReferenceEquals(mutationBlock, saveBlock)
        )
        {
            return ReferenceEquals(mutationBlock, saveBlock)
                && mutation.Syntax.SpanStart < save.Syntax.SpanStart;
        }

        return CanReach(mutationBlock, saveBlock);
    }

    private static BasicBlock? FindContainingBlock(ControlFlowGraph flowGraph, IOperation operation)
    {
        foreach (var block in flowGraph.Blocks)
        {
            if (
                block.Operations.Any(root => ContainsOperation(root, operation))
                || block.BranchValue != null && ContainsOperation(block.BranchValue, operation)
            )
            {
                return block;
            }
        }

        return null;
    }

    private static bool ContainsOperation(IOperation root, IOperation target)
    {
        if (
            ReferenceEquals(root, target)
            || root.Syntax.SyntaxTree == target.Syntax.SyntaxTree
                && root.Syntax.Span.Contains(target.Syntax.Span)
        )
        {
            return true;
        }

        return root.ChildOperations.Any(child => ContainsOperation(child, target));
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

    private static bool CanReach(
        BasicBlock start,
        BasicBlock target,
        IMethodSymbol method,
        int? assumedParameterOrdinal,
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

            EnqueueFeasibleSuccessors(
                current,
                pending,
                method,
                assumedParameterOrdinal,
                assumedValue
            );
        }

        return false;
    }

    private static bool CanReachAvoiding(
        BasicBlock start,
        BasicBlock target,
        BasicBlock excluded
    ) => CanReachAvoiding(start, target, excluded, null, null, false);

    private static bool CanReachAvoiding(
        BasicBlock start,
        BasicBlock target,
        BasicBlock excluded,
        IMethodSymbol? method,
        int? assumedParameterOrdinal,
        bool assumedValue
    )
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

            EnqueueFeasibleSuccessors(
                current,
                pending,
                method,
                assumedParameterOrdinal,
                assumedValue
            );
        }

        return false;
    }

    private static void EnqueueFeasibleSuccessors(
        BasicBlock block,
        Queue<BasicBlock> pending,
        IMethodSymbol? method,
        int? assumedParameterOrdinal,
        bool assumedValue
    )
    {
        if (
            TryEvaluateHelperCondition(
                block.BranchValue,
                method,
                assumedParameterOrdinal,
                assumedValue,
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

    private static bool TryEvaluateHelperCondition(
        IOperation? operation,
        IMethodSymbol? method,
        int? assumedParameterOrdinal,
        bool assumedValue,
        out bool value
    )
    {
        if (operation?.ConstantValue is { HasValue: true, Value: bool constant })
        {
            value = constant;
            return true;
        }

        while (operation is IParenthesizedOperation parenthesized)
            operation = parenthesized.Operand;
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;

        if (
            operation is IParameterReferenceOperation parameterReference
            && method != null
            && assumedParameterOrdinal.HasValue
            && parameterReference.Parameter.Ordinal == assumedParameterOrdinal.Value
            && SymbolEqualityComparer.Default.Equals(
                parameterReference.Parameter.ContainingSymbol,
                method
            )
        )
        {
            value = assumedValue;
            return true;
        }

        if (
            operation
                is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: var operand }
            && TryEvaluateHelperCondition(
                operand,
                method,
                assumedParameterOrdinal,
                assumedValue,
                out var operandValue
            )
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
                    LeftOperand: var leftOperand,
                    RightOperand: var rightOperand,
                } binary
            && TryEvaluateHelperCondition(
                leftOperand,
                method,
                assumedParameterOrdinal,
                assumedValue,
                out var leftValue
            )
            && TryEvaluateHelperCondition(
                rightOperand,
                method,
                assumedParameterOrdinal,
                assumedValue,
                out var rightValue
            )
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

    private static bool IsDefinitelyOverwritten(
        HelperMutation mutation,
        IOperation mutationOperation,
        IEnumerable<ISimpleAssignmentOperation> assignments,
        IMethodSymbol method,
        IEnumerable<IOperation> subsequentSaves,
        ControlFlowGraph? flowGraph
    )
    {
        var assignmentList = assignments.ToList();
        var saveList = subsequentSaves.ToList();
        var directBlock = FindDirectOperationBlock(mutationOperation);
        if (
            directBlock != null
            && assignmentList.Any(assignment =>
                FindDirectOperationBlock(assignment) == directBlock
                && assignment.Syntax.SpanStart > mutation.Position
                && saveList.All(save =>
                    save.Syntax.SpanStart > assignment.Syntax.SpanStart
                    || FindDirectOperationBlock(save) != directBlock
                )
                && assignment.Target is IPropertyReferenceOperation target
                && SymbolEqualityComparer.Default.Equals(target.Property, mutation.Property)
                && LostUpdateOperationFacts.TryGetRootSymbol(target.Instance, out var rootSymbol)
                && SymbolEqualityComparer.Default.Equals(rootSymbol, mutation.Target.Symbol)
                && !LostUpdateOperationFacts.ContainsPropertyRead(
                    assignment.Value,
                    target.Property,
                    rootSymbol
                )
                && IsDefinitelyNonThrowing(assignment.Value)
            )
        )
        {
            return true;
        }

        if (flowGraph == null)
            return false;

        var mutationBlock = FindContainingBlock(flowGraph, mutationOperation);
        if (mutationBlock == null)
            return false;

        var targets = new List<(BasicBlock Block, int Position)>();
        foreach (var subsequentSave in saveList)
        {
            var saveBlock = FindContainingBlock(flowGraph, subsequentSave);
            if (saveBlock == null)
                return false;
            targets.Add((saveBlock, subsequentSave.Syntax.SpanStart));
        }
        targets.Add((flowGraph.Blocks[flowGraph.Blocks.Length - 1], int.MaxValue));

        foreach (var assignment in assignmentList)
        {
            if (
                assignment.Syntax.SpanStart <= mutation.Position
                || assignment.Target is not IPropertyReferenceOperation target
                || !SymbolEqualityComparer.Default.Equals(target.Property, mutation.Property)
                || !LostUpdateOperationFacts.TryGetRootSymbol(target.Instance, out var rootSymbol)
                || !SymbolEqualityComparer.Default.Equals(rootSymbol, mutation.Target.Symbol)
                || LostUpdateOperationFacts.ContainsPropertyRead(
                    assignment.Value,
                    target.Property,
                    rootSymbol
                )
                || !IsDefinitelyNonThrowing(assignment.Value)
            )
            {
                continue;
            }

            var overwriteBlock = FindContainingBlock(flowGraph, assignment);
            if (
                overwriteBlock != null
                && targets.All(target =>
                    IsOverwriteUnavoidableBeforeTarget(
                        mutationBlock,
                        mutation.Position,
                        overwriteBlock,
                        assignment.Syntax.SpanStart,
                        target.Block,
                        target.Position
                    )
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IBlockOperation? FindDirectOperationBlock(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IBlockOperation block)
                return block;
            if (current is IConditionalOperation or ILoopOperation)
                return null;
        }

        return null;
    }

    private static bool IsOverwriteUnavoidableBeforeTarget(
        BasicBlock mutationBlock,
        int mutationPosition,
        BasicBlock overwriteBlock,
        int overwritePosition,
        BasicBlock targetBlock,
        int targetPosition
    )
    {
        if (ReferenceEquals(mutationBlock, targetBlock))
        {
            return ReferenceEquals(overwriteBlock, mutationBlock)
                && overwritePosition > mutationPosition
                && overwritePosition < targetPosition;
        }

        return ReferenceEquals(overwriteBlock, mutationBlock)
                && overwritePosition > mutationPosition
            || ReferenceEquals(overwriteBlock, targetBlock) && overwritePosition < targetPosition
            || !ReferenceEquals(overwriteBlock, mutationBlock)
                && !ReferenceEquals(overwriteBlock, targetBlock)
                && CanReach(mutationBlock, overwriteBlock)
                && CanReach(overwriteBlock, targetBlock)
                && !CanReachAvoiding(mutationBlock, targetBlock, overwriteBlock);
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

    private static void AddMutation(
        IOperation targetOperation,
        IOperation mutation,
        IMethodSymbol method,
        ImmutableArray<HelperMutation>.Builder mutations,
        Dictionary<int, IOperation> mutationOperations
    )
    {
        if (
            targetOperation is IPropertyReferenceOperation target
            && TryGetHelperTarget(target.Instance, method, out var helperTarget)
        )
        {
            mutations.Add(
                new HelperMutation(
                    helperTarget,
                    target.Property,
                    target.Syntax.GetLocation(),
                    mutation.Syntax.SpanStart
                )
            );
            mutationOperations[mutation.Syntax.SpanStart] = mutation;
        }
    }
}

internal readonly struct HelperTarget
{
    internal HelperTarget(IParameterSymbol parameter)
    {
        Symbol = parameter;
        ParameterOrdinal = parameter.Ordinal;
    }

    internal HelperTarget(ISymbol capturedSymbol)
    {
        Symbol = capturedSymbol;
        ParameterOrdinal = null;
    }

    internal ISymbol Symbol { get; }
    internal int? ParameterOrdinal { get; }

    internal ITypeSymbol? Type =>
        Symbol switch
        {
            IParameterSymbol parameter => parameter.Type,
            ILocalSymbol local => local.Type,
            IFieldSymbol @field => @field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
}

internal readonly struct HelperTransaction
{
    internal HelperTransaction(
        HelperTarget target,
        int position,
        ImmutableArray<int> protectedSavePositions = default,
        int? conditionParameterOrdinal = null,
        bool conditionValue = false
    )
    {
        Target = target;
        Position = position;
        ProtectedSavePositions = protectedSavePositions.IsDefault
            ? ImmutableArray<int>.Empty
            : protectedSavePositions;
        ConditionParameterOrdinal = conditionParameterOrdinal;
        ConditionValue = conditionValue;
    }

    internal HelperTarget Target { get; }
    internal int Position { get; }
    internal ImmutableArray<int> ProtectedSavePositions { get; }
    internal int? ConditionParameterOrdinal { get; }
    internal bool ConditionValue { get; }

    internal HelperTransaction WithProtectedSavePositions(ImmutableArray<int> savePositions)
    {
        return new HelperTransaction(
            Target,
            Position,
            savePositions,
            ConditionParameterOrdinal,
            ConditionValue
        );
    }
}

internal readonly struct HelperSave
{
    internal HelperSave(
        HelperTarget target,
        Location location,
        int position,
        int? conditionParameterOrdinal = null,
        bool conditionValue = false
    )
    {
        Target = target;
        Location = location;
        Position = position;
        ConditionParameterOrdinal = conditionParameterOrdinal;
        ConditionValue = conditionValue;
    }

    internal HelperTarget Target { get; }
    internal Location Location { get; }
    internal int Position { get; }
    internal int? ConditionParameterOrdinal { get; }
    internal bool ConditionValue { get; }
}

internal readonly struct HelperMutation
{
    internal HelperMutation(
        HelperTarget target,
        IPropertySymbol property,
        Location location,
        int position,
        ImmutableArray<HelperSave> subsequentSaves = default,
        int? conditionParameterOrdinal = null,
        bool conditionValue = false
    )
    {
        Target = target;
        Property = property;
        Location = location;
        Position = position;
        SubsequentSaves = subsequentSaves.IsDefault
            ? ImmutableArray<HelperSave>.Empty
            : subsequentSaves;
        ConditionParameterOrdinal = conditionParameterOrdinal;
        ConditionValue = conditionValue;
    }

    internal HelperTarget Target { get; }
    internal IPropertySymbol Property { get; }
    internal Location Location { get; }
    internal int Position { get; }
    internal ImmutableArray<HelperSave> SubsequentSaves { get; }
    internal int? ConditionParameterOrdinal { get; }
    internal bool ConditionValue { get; }

    internal HelperMutation WithSubsequentSaves(ImmutableArray<HelperSave> saves)
    {
        return new HelperMutation(
            Target,
            Property,
            Location,
            Position,
            saves,
            ConditionParameterOrdinal,
            ConditionValue
        );
    }

    internal HelperMutation WithCondition(int conditionParameterOrdinal, bool conditionValue)
    {
        return new HelperMutation(
            Target,
            Property,
            Location,
            Position,
            SubsequentSaves,
            conditionParameterOrdinal,
            conditionValue
        );
    }
}

internal sealed class OperationCollector : OperationWalker
{
    internal readonly List<IInvocationOperation> Invocations = new();
    internal readonly List<ISimpleAssignmentOperation> SimpleAssignments = new();
    internal readonly List<ICompoundAssignmentOperation> CompoundAssignments = new();
    internal readonly List<ICoalesceAssignmentOperation> CoalesceAssignments = new();
    internal readonly List<IIncrementOrDecrementOperation> Increments = new();
    internal readonly List<IVariableDeclaratorOperation> Declarators = new();
    internal readonly List<IPropertyReferenceOperation> PropertyReferences = new();
    internal readonly List<ILocalReferenceOperation> LocalReferences = new();
    internal bool HasConditionalControlFlow { get; private set; }
    internal bool HasUnsupportedHelperControlFlow { get; private set; }

    public override void VisitConditional(IConditionalOperation operation)
    {
        if (operation.Condition.ConstantValue is { HasValue: true, Value: bool condition })
        {
            Visit(operation.Condition);
            Visit(condition ? operation.WhenTrue : operation.WhenFalse);
            return;
        }

        HasConditionalControlFlow = true;
        base.VisitConditional(operation);
    }

    public override void VisitBinaryOperator(IBinaryOperation operation)
    {
        if (
            operation.LeftOperand.ConstantValue is { HasValue: true, Value: bool leftValue }
            && (
                operation.OperatorKind == BinaryOperatorKind.ConditionalAnd && !leftValue
                || operation.OperatorKind == BinaryOperatorKind.ConditionalOr && leftValue
            )
        )
        {
            Visit(operation.LeftOperand);
            return;
        }

        base.VisitBinaryOperator(operation);
    }

    public override void VisitCoalesce(ICoalesceOperation operation)
    {
        if (operation.Value.ConstantValue is { HasValue: true, Value: not null })
        {
            Visit(operation.Value);
            return;
        }

        base.VisitCoalesce(operation);
    }

    public override void VisitWhileLoop(IWhileLoopOperation operation)
    {
        if (
            operation.ConditionIsTop
            && operation.Condition is { ConstantValue: { HasValue: true, Value: false } } condition
        )
        {
            Visit(condition);
            return;
        }

        base.VisitWhileLoop(operation);
    }

    public override void VisitSwitch(ISwitchOperation operation)
    {
        HasConditionalControlFlow = true;
        HasUnsupportedHelperControlFlow = true;
        base.VisitSwitch(operation);
    }

    public override void VisitSwitchExpression(ISwitchExpressionOperation operation)
    {
        HasConditionalControlFlow = true;
        HasUnsupportedHelperControlFlow = true;
        base.VisitSwitchExpression(operation);
    }

    public override void VisitTry(ITryOperation operation)
    {
        HasConditionalControlFlow = true;
        HasUnsupportedHelperControlFlow = true;
        base.VisitTry(operation);
    }

    public override void VisitBranch(IBranchOperation operation)
    {
        HasConditionalControlFlow = true;
        HasUnsupportedHelperControlFlow = true;
        base.VisitBranch(operation);
    }

    public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
    {
        // Nested executable bodies are analyzed by their own operation-block callbacks.
    }

    public override void VisitLocalFunction(ILocalFunctionOperation operation)
    {
        // A declaration is not execution; call-site summaries handle proven local/private helpers.
    }

    public override void VisitInvocation(IInvocationOperation operation)
    {
        Invocations.Add(operation);
        base.VisitInvocation(operation);
    }

    public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
    {
        SimpleAssignments.Add(operation);
        base.VisitSimpleAssignment(operation);
    }

    public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
    {
        CompoundAssignments.Add(operation);
        base.VisitCompoundAssignment(operation);
    }

    public override void VisitCoalesceAssignment(ICoalesceAssignmentOperation operation)
    {
        CoalesceAssignments.Add(operation);
        base.VisitCoalesceAssignment(operation);
    }

    public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
    {
        Increments.Add(operation);
        base.VisitIncrementOrDecrement(operation);
    }

    public override void VisitPropertyReference(IPropertyReferenceOperation operation)
    {
        PropertyReferences.Add(operation);
        base.VisitPropertyReference(operation);
    }

    public override void VisitLocalReference(ILocalReferenceOperation operation)
    {
        LocalReferences.Add(operation);
        base.VisitLocalReference(operation);
    }

    public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
    {
        Declarators.Add(operation);
        base.VisitVariableDeclarator(operation);
    }
}
