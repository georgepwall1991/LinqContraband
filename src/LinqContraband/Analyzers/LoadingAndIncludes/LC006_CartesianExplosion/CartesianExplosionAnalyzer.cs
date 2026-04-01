using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

/// <summary>
/// Analyzes Entity Framework queries that Include multiple collection navigations, causing Cartesian product data duplication. Diagnostic ID: LC006
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When multiple collection navigations are loaded in a single query using Include(),
/// Entity Framework generates a SQL query with multiple JOINs that creates a Cartesian product. This causes geometric
/// data duplication where the result set size equals the product of all collection sizes (e.g., 10 Orders with 5 Items
/// each and 3 Payments each returns 150 rows instead of 18). This wastes bandwidth, memory, and database resources.
/// Use AsSplitQuery() to separate into distinct SQL queries or manually load collections separately.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CartesianExplosionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC006";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Cartesian Explosion Risk: Multiple Collection Includes";

    private static readonly LocalizableString MessageFormat =
        "Including multiple collections ('{0}') in a single query causes Cartesian Explosion. Use AsSplitQuery().";

    private static readonly LocalizableString Description =
        "Loading multiple collections in a single query causes geometric data duplication. Use .AsSplitQuery() to separate them into distinct SQL queries.";

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

        if (method.Name != "Include" || method.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
            return;

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel == null) return;

        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax) return;
        if (!TryGetIncludedNavigation(invocationSyntax, semanticModel, out var currentNavigation)) return;

        var chain = AnalyzeIncludeChain(invocationSyntax, semanticModel);
        if (HasSplitQueryDownstream(invocationSyntax, semanticModel)) return;
        if (chain.HasSplitQuery) return;

        if (chain.CollectionIncludes.Count > 1)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), currentNavigation));
        }
    }

    private static IncludeChainAnalysis AnalyzeIncludeChain(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel)
    {
        var result = new IncludeChainAnalysis();
        InvocationExpressionSyntax? current = invocationSyntax;

        while (current?.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var symbol = semanticModel.GetSymbolInfo(current).Symbol as IMethodSymbol;
            if (symbol?.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
            {
                current = memberAccess.Expression as InvocationExpressionSyntax;
                continue;
            }

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName == "AsSplitQuery")
            {
                result.HasSplitQuery = true;
            }
            else if (methodName == "Include" &&
                     TryGetIncludedNavigation(current, semanticModel, out var navigation))
            {
                result.CollectionIncludes.Add(navigation);
            }

            current = memberAccess.Expression as InvocationExpressionSyntax;
        }

        result.CollectionIncludes.Reverse();
        return result;
    }

    private static bool HasSplitQueryDownstream(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var current = invocation.Parent;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax parentInvocation &&
                parentInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "AsSplitQuery" &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol method &&
                method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool TryGetIncludedNavigation(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        out string navigationName)
    {
        navigationName = string.Empty;
        if (invocationSyntax.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var lambdaExpression = invocationSyntax.ArgumentList.Arguments.Last().Expression;
        var lambdaBody = lambdaExpression switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Body,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.Body,
            _ => null
        };

        if (lambdaBody is MemberAccessExpressionSyntax memberAccess)
        {
            var propertySymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
            var propertyType = propertySymbol?.Type ?? semanticModel.GetTypeInfo(memberAccess).Type;
            if (propertyType == null || !IsCollection(propertyType)) return false;

            navigationName = memberAccess.Name.Identifier.Text;
            return true;
        }

        return false;
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return false;
        // Arrays are collections
        if (type.TypeKind == TypeKind.Array) return true;

        if (type is INamedTypeSymbol namedType)
        {
            var ns = namedType.ContainingNamespace?.ToString();

            // Check for System.Collections.Generic types with namespace verification
            if (ns == "System.Collections.Generic" && namedType.IsGenericType)
            {
                return namedType.Name is "List" or "IList" or "IEnumerable" or "ICollection"
                    or "HashSet" or "ISet" or "IReadOnlyList" or "IReadOnlyCollection";
            }
        }

        // Also check interfaces for IEnumerable<T> implementation
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == "IEnumerable" && iface.IsGenericType &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic")
            {
                return true;
            }
        }

        return false;
    }

    private sealed class IncludeChainAnalysis
    {
        public bool HasSplitQuery { get; set; }

        public List<string> CollectionIncludes { get; } = new();
    }
}
