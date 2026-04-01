using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Analyzes Entity Framework Core queries to detect loading entire entities when only a few properties are accessed.
/// Diagnostic ID: LC017
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Loading entire entities when only 1-2 properties are needed wastes bandwidth,
/// memory, and CPU. For large entities with 10+ properties, using .Select() projection can dramatically reduce
/// data transfer and improve performance.</para>
/// <para><b>Conservative detection:</b> This analyzer only reports when clearly wasteful patterns are detected:
/// entities with 10+ properties where only 1-2 are accessed within local scope.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WholeEntityProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC017";
    private const string Category = "Performance";
    private const int MinPropertyThreshold = 10; // Only flag entities with 10+ properties
    private const int MaxAccessedProperties = 2; // Only flag when 1-2 properties accessed

    private static readonly LocalizableString Title = "Performance: Consider using Select() projection";

    private static readonly LocalizableString MessageFormat =
        "Query loads entire '{0}' entity but only {1} of {2} properties are accessed. Consider using .Select() projection.";

    private static readonly LocalizableString Description =
        "Loading entire entities when only a few properties are needed wastes bandwidth and memory. " +
        "Use .Select() to project only the required fields for improved performance.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // 1. Must be a collection materializer (ToList, ToArray) - skip single-entity queries
        if (!IsCollectionMaterializer(method)) return;

        // 2. Analyze the query chain
        var analysis = AnalyzeQueryChain(invocation);
        if (!analysis.IsEfQuery) return;
        if (analysis.HasSelect) return; // Already using projection
        if (analysis.EntityType == null) return;

        // 3. Check entity has enough properties to matter (10+)
        var properties = GetEntityProperties(analysis.EntityType);
        if (properties.Count < MinPropertyThreshold) return;

        // 4. Find the variable assignment
        var variableInfo = FindVariableAssignment(invocation);
        if (variableInfo == null) return;

        // 5. Analyze usage in a single pass to avoid repeated full-body scans
        var usage = AnalyzeVariableUsage(invocation, variableInfo.Value.Symbol, analysis.EntityType);
        if (usage.HasEscapingUsage) return;

        // 9. Conservative: only flag if 1-2 properties accessed
        if (usage.AccessedProperties.Count > MaxAccessedProperties) return;
        if (usage.AccessedProperties.Count == 0) return; // No properties accessed, might be passed somewhere

        // Report diagnostic
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                analysis.EntityType.Name,
                usage.AccessedProperties.Count,
                properties.Count));
    }

    private static bool IsCollectionMaterializer(IMethodSymbol method)
    {
        // Only flag collection materializers - single entity queries have less overhead
        return method.Name is "ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync";
    }

    private QueryChainAnalysis AnalyzeQueryChain(IInvocationOperation invocation)
    {
        var result = new QueryChainAnalysis();
        var current = invocation.GetInvocationReceiver();

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation prevInvocation)
            {
                if (prevInvocation.TargetMethod.Name == "Select") result.HasSelect = true;
                current = prevInvocation.GetInvocationReceiver(false);
                continue;
            }

            // Terminal node: check if the source is a DbSet
            TryExtractDbSetInfo(current, result);
            break;
        }

        return result;
    }

    private static void TryExtractDbSetInfo(IOperation operation, QueryChainAnalysis result)
    {
        var type = operation switch
        {
            IPropertyReferenceOperation propRef => propRef.Type,
            IFieldReferenceOperation fieldRef => fieldRef.Type,
            _ => operation.Type
        };

        if (type != null && type.IsDbSet())
        {
            result.IsEfQuery = true;
            result.EntityType = GetElementType(type);
        }
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
        {
            return namedType.TypeArguments[0];
        }
        return null;
    }

    private static List<IPropertySymbol> GetEntityProperties(ITypeSymbol entityType)
    {
        var properties = new List<IPropertySymbol>();
        var current = entityType;

        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop &&
                    prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    prop.GetMethod != null)
                {
                    properties.Add(prop);
                }
            }
            current = current.BaseType;
        }

        return properties;
    }

    private static (ILocalSymbol Symbol, IOperation Declaration)? FindVariableAssignment(IInvocationOperation invocation)
    {
        // Walk up to find assignment
        var parent = invocation.Parent;

        while (parent != null)
        {
            if (parent is IVariableDeclaratorOperation declarator)
            {
                return (declarator.Symbol, declarator);
            }

            if (parent is ISimpleAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation localRef)
            {
                return (localRef.Local, assignment);
            }

            // Don't go beyond statement level for simple cases
            if (parent is IExpressionStatementOperation) break;
            if (parent is IReturnOperation) break;

            parent = parent.Parent;
        }

        return null;
    }

    private static VariableUsageAnalysis AnalyzeVariableUsage(
        IInvocationOperation invocation,
        ILocalSymbol variable,
        ITypeSymbol entityType)
    {
        var result = new VariableUsageAnalysis();
        var root = FindMethodBody(invocation);
        if (root == null)
        {
            result.HasEscapingUsage = true;
            return result;
        }

        var foreachLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var manualIterationLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (var descendant in root.Descendants())
        {
            switch (descendant)
            {
                case IForEachLoopOperation forEach when
                    forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                    SymbolEqualityComparer.Default.Equals(collectionRef.Local, variable):
                    foreach (var local in forEach.Locals)
                        foreachLocals.Add(local);
                    break;

                case IVariableDeclaratorOperation declarator when
                    declarator.Initializer != null &&
                    IsIndexedAccessOf(declarator.Initializer.Value, variable):
                    manualIterationLocals.Add(declarator.Symbol);
                    break;

                case ISimpleAssignmentOperation assignment when
                    assignment.Target is ILocalReferenceOperation targetLocal &&
                    IsIndexedAccessOf(assignment.Value, variable):
                    manualIterationLocals.Add(targetLocal.Local);
                    break;
            }
        }

        foreach (var descendant in root.Descendants())
        {
            switch (descendant)
            {
                case IReturnOperation returnOperation when
                    returnOperation.ReturnedValue != null &&
                    IsDirectVariableEscape(returnOperation.ReturnedValue, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IInvocationOperation call when call != invocation &&
                    call.Arguments.Any(arg => IsDirectVariableEscape(arg.Value, variable, foreachLocals, manualIterationLocals)):
                    result.HasEscapingUsage = true;
                    break;

                case IAnonymousFunctionOperation lambda when
                    LambdaDirectlyReferences(lambda, variable, foreachLocals, manualIterationLocals):
                    result.HasEscapingUsage = true;
                    break;

                case IPropertyReferenceOperation propertyReference when
                    IsPropertyOfType(propertyReference.Property, entityType) &&
                    IsTrackedEntityReference(propertyReference.Instance, variable, foreachLocals, manualIterationLocals):
                    result.AccessedProperties.Add(propertyReference.Property.Name);
                    break;
            }

            if (result.HasEscapingUsage) return result;
        }

        CollectSyntaxBasedPropertyAccesses(invocation, variable, entityType, foreachLocals, manualIterationLocals, result.AccessedProperties);
        return result;
    }

    private static bool IsTrackedEntityReference(
        IOperation? operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        if (operation == null) return false;

        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is ILocalReferenceOperation localReference)
        {
            return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                   foreachLocals.Contains(localReference.Local) ||
                   manualIterationLocals.Contains(localReference.Local);
        }

        return false;
    }

    private static bool IsDirectVariableEscape(
        IOperation operation,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is not ILocalReferenceOperation localReference) return false;

        return SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
               foreachLocals.Contains(localReference.Local) ||
               manualIterationLocals.Contains(localReference.Local);
    }

    private static bool LambdaDirectlyReferences(
        IAnonymousFunctionOperation lambda,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals)
    {
        foreach (var descendant in lambda.Descendants())
        {
            if (descendant is not ILocalReferenceOperation localReference) continue;

            if (SymbolEqualityComparer.Default.Equals(localReference.Local, variable) ||
                foreachLocals.Contains(localReference.Local) ||
                manualIterationLocals.Contains(localReference.Local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPropertyOfType(IPropertySymbol property, ITypeSymbol entityType)
    {
        if (SymbolEqualityComparer.Default.Equals(property.ContainingType, entityType)) return true;
        if (entityType.AllInterfaces.Contains(property.ContainingType, SymbolEqualityComparer.Default)) return true;
        return InheritsFrom(entityType, property.ContainingType);
    }

    private static void CollectSyntaxBasedPropertyAccesses(
        IInvocationOperation invocation,
        ILocalSymbol variable,
        ITypeSymbol entityType,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        HashSet<string> accessedProperties)
    {
        var semanticModel = invocation.SemanticModel;
        if (semanticModel == null) return;

        var scope = invocation.Syntax.FirstAncestorOrSelf<MethodDeclarationSyntax>() as SyntaxNode ??
                    invocation.Syntax.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() ??
                    invocation.Syntax.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() as SyntaxNode;
        if (scope == null) return;

        foreach (var conditionalAccess in scope.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
        {
            if (!IsTrackedEntitySyntax(conditionalAccess.Expression, variable, foreachLocals, manualIterationLocals, semanticModel))
                continue;

            if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                semanticModel.GetSymbolInfo(memberBinding).Symbol is IPropertySymbol property &&
                IsPropertyOfType(property, entityType))
            {
                accessedProperties.Add(property.Name);
            }
        }

        foreach (var memberAccess in scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Expression is not ElementAccessExpressionSyntax elementAccess) continue;
            if (!IsCollectionElementAccess(elementAccess, variable, semanticModel)) continue;

            if (semanticModel.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property &&
                IsPropertyOfType(property, entityType))
            {
                accessedProperties.Add(property.Name);
            }
        }
    }

    private static bool IsTrackedEntitySyntax(
        ExpressionSyntax expression,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        SemanticModel semanticModel)
    {
        if (expression is ElementAccessExpressionSyntax elementAccess)
            return IsCollectionElementAccess(elementAccess, variable, semanticModel);

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol as ILocalSymbol;
        if (symbol == null) return false;

        return SymbolEqualityComparer.Default.Equals(symbol, variable) ||
               foreachLocals.Contains(symbol) ||
               manualIterationLocals.Contains(symbol);
    }

    private static bool IsCollectionElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        ILocalSymbol variable,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(elementAccess.Expression).Symbol as ILocalSymbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, variable);
    }

    private sealed class VariableUsageAnalysis
    {
        public HashSet<string> AccessedProperties { get; } = new();

        public bool HasEscapingUsage { get; set; }
    }

    private static bool IsManualIterationVariableOver(ILocalSymbol iterationVar, ILocalSymbol collectionVar, IOperation root)
    {
        // Detect patterns like: var item = collection[i];
        foreach (var descendant in root.Descendants())
        {
            if (descendant is IVariableDeclaratorOperation decl &&
                SymbolEqualityComparer.Default.Equals(decl.Symbol, iterationVar))
            {
                if (decl.Initializer != null && IsIndexedAccessOf(decl.Initializer.Value, collectionVar))
                    return true;
            }
            if (descendant is ISimpleAssignmentOperation assign &&
                assign.Target is ILocalReferenceOperation localRef &&
                SymbolEqualityComparer.Default.Equals(localRef.Local, iterationVar))
            {
                if (IsIndexedAccessOf(assign.Value, collectionVar))
                    return true;
            }
        }
        return false;
    }

    private static bool IsIndexedAccessOf(IOperation operation, ILocalSymbol collectionVar)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is IPropertyReferenceOperation propRef && propRef.Arguments.Length > 0)
        {
            var instance = propRef.Instance?.UnwrapConversions();
            if (instance is ILocalReferenceOperation localRef &&
                SymbolEqualityComparer.Default.Equals(localRef.Local, collectionVar))
                return true;
        }
        return false;
    }

    private static bool IsForEachVariableOver(ILocalSymbol iterationVar, ILocalSymbol collectionVar, IOperation root)
    {
        foreach (var descendant in root.Descendants())
        {
            if (descendant is IForEachLoopOperation forEach)
            {
                // Check if collection references our variable
                if (forEach.Collection.UnwrapConversions() is ILocalReferenceOperation collectionRef &&
                    SymbolEqualityComparer.Default.Equals(collectionRef.Local, collectionVar))
                {
                    // Check if iteration variable matches
                    if (forEach.Locals.Any(l => SymbolEqualityComparer.Default.Equals(l, iterationVar)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool InheritsFrom(ITypeSymbol type, ITypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static IOperation? FindMethodBody(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current is IMethodBodyOperation ||
                current is IBlockOperation { Parent: IMethodBodyOperation } ||
                current is ILocalFunctionOperation)
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    private class QueryChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool HasSelect { get; set; }
        public ITypeSymbol? EntityType { get; set; }
    }
}
