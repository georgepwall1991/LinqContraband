using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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
        DiagnosticSeverity.Warning,
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

        // 5. Check if entity is returned from method (can't track usage)
        if (IsEntityReturned(invocation, variableInfo.Value.Symbol)) return;

        // 6. Check if entity is passed to external methods
        if (IsPassedToMethod(invocation, variableInfo.Value.Symbol)) return;

        // 7. Check if entity is used in lambda/delegate
        if (IsUsedInLambda(invocation, variableInfo.Value.Symbol)) return;

        // 8. Count actual property accesses
        var accessedProperties = CountPropertyAccesses(invocation, variableInfo.Value.Symbol, analysis.EntityType);

        // 9. Conservative: only flag if 1-2 properties accessed
        if (accessedProperties.Count > MaxAccessedProperties) return;
        if (accessedProperties.Count == 0) return; // No properties accessed, might be passed somewhere

        // Report diagnostic
        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                analysis.EntityType.Name,
                accessedProperties.Count,
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
                var methodName = prevInvocation.TargetMethod.Name;

                if (methodName == "Select") result.HasSelect = true;

                // Move up the chain
                current = prevInvocation.GetInvocationReceiver(false);
            }
            else if (current is IPropertyReferenceOperation propRef)
            {
                if (propRef.Type.IsDbSet())
                {
                    result.IsEfQuery = true;
                    result.EntityType = GetElementType(propRef.Type);
                }
                break;
            }
            else if (current is IFieldReferenceOperation fieldRef)
            {
                if (fieldRef.Type.IsDbSet())
                {
                    result.IsEfQuery = true;
                    result.EntityType = GetElementType(fieldRef.Type);
                }
                break;
            }
            else
            {
                if (current.Type.IsDbSet())
                {
                    result.IsEfQuery = true;
                    result.EntityType = GetElementType(current.Type);
                }
                break;
            }
        }

        return result;
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

    private static bool IsEntityReturned(IInvocationOperation invocation, ILocalSymbol variable)
    {
        // Find containing method body
        var root = FindMethodBody(invocation);
        if (root == null) return true; // Conservative: can't analyze, assume returned

        // Check for return statements using this variable
        foreach (var descendant in root.Descendants())
        {
            if (descendant is IReturnOperation returnOp &&
                returnOp.ReturnedValue != null)
            {
                if (ReferencesVariable(returnOp.ReturnedValue, variable))
                    return true;
            }
        }

        return false;
    }

    private static bool IsPassedToMethod(IInvocationOperation invocation, ILocalSymbol variable)
    {
        var root = FindMethodBody(invocation);
        if (root == null) return true;

        foreach (var descendant in root.Descendants())
        {
            if (descendant is IInvocationOperation call && call != invocation)
            {
                foreach (var arg in call.Arguments)
                {
                    if (ReferencesVariable(arg.Value, variable))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsUsedInLambda(IInvocationOperation invocation, ILocalSymbol variable)
    {
        var root = FindMethodBody(invocation);
        if (root == null) return true;

        foreach (var descendant in root.Descendants())
        {
            if (descendant is IAnonymousFunctionOperation lambda)
            {
                foreach (var lambdaDescendant in lambda.Descendants())
                {
                    if (lambdaDescendant is ILocalReferenceOperation localRef &&
                        SymbolEqualityComparer.Default.Equals(localRef.Local, variable))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static HashSet<string> CountPropertyAccesses(
        IInvocationOperation invocation,
        ILocalSymbol variable,
        ITypeSymbol entityType)
    {
        var accessedProperties = new HashSet<string>();
        var root = FindMethodBody(invocation);
        if (root == null) return accessedProperties;

        // Find property accesses on the variable or its elements (in foreach)
        foreach (var descendant in root.Descendants())
        {
            if (descendant is IPropertyReferenceOperation propRef)
            {
                // Check if this is a property of the entity type
                if (!SymbolEqualityComparer.Default.Equals(propRef.Property.ContainingType, entityType) &&
                    !entityType.AllInterfaces.Contains(propRef.Property.ContainingType, SymbolEqualityComparer.Default) &&
                    !InheritsFrom(entityType, propRef.Property.ContainingType))
                {
                    continue;
                }

                // Check if accessed on our variable or a foreach iteration variable
                var instance = propRef.Instance?.UnwrapConversions();

                if (instance is ILocalReferenceOperation localRef)
                {
                    // Direct access on variable or foreach variable iterating over our collection
                    if (SymbolEqualityComparer.Default.Equals(localRef.Local, variable))
                    {
                        accessedProperties.Add(propRef.Property.Name);
                    }
                    else if (IsForEachVariableOver(localRef.Local, variable, root) || 
                             IsManualIterationVariableOver(localRef.Local, variable, root))
                    {
                        accessedProperties.Add(propRef.Property.Name);
                    }
                }
            }
        }

        return accessedProperties;
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

    private static bool ReferencesVariable(IOperation operation, ILocalSymbol variable)
    {
        var unwrapped = operation.UnwrapConversions();

        if (unwrapped is ILocalReferenceOperation localRef &&
            SymbolEqualityComparer.Default.Equals(localRef.Local, variable))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ReferencesVariable(child, variable))
                return true;
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
