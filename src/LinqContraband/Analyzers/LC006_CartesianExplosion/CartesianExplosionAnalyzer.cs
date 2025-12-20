using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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

        var methodName = method.Name;
        if ((methodName != "Include" && methodName != "ThenInclude") ||
            method.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore")
            return;

        // We only flag top-level Include chaining. ThenInclude deep chains are considered valid.
        if (methodName == "ThenInclude") return;

        ITypeSymbol? propertyType = null;
        if (method.TypeArguments.Length >= 2)
            propertyType = method.TypeArguments[method.TypeArguments.Length - 1];

        // For string-based Include (no type args), we can't determine collection vs scalar; avoid flagging to prevent false positives.
        if (propertyType == null) return;

        if (!IsCollection(propertyType)) return;

        // Now check the chain backwards. Handle extension method syntax.
        var current = invocation.GetInvocationReceiver();

        var foundSplitQuery = false;
        var previousCollectionIncludes = 0;

        while (current != null)
        {
            // Unwrap implicit conversions
            while (current is IConversionOperation conversion) current = conversion.Operand;

            if (current is IInvocationOperation prevInvocation)
            {
                var prevMethod = prevInvocation.TargetMethod;

                if (prevMethod.Name == "AsSplitQuery" &&
                    prevMethod.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                {
                    foundSplitQuery = true;
                    break;
                }

                if (prevMethod.Name == "Include" &&
                    prevMethod.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
                {
                    // Check if this previous include was ALSO a collection
                    ITypeSymbol? prevPropType = null;
                    if (prevMethod.TypeArguments.Length >= 2)
                        prevPropType = prevMethod.TypeArguments[prevMethod.TypeArguments.Length - 1];

                    // Only count as collection include if we can verify it's a collection type
                    // When prevPropType is null (string-based Include), we can't determine, so skip counting
                    if (prevPropType != null && IsCollection(prevPropType)) previousCollectionIncludes++;
                }

                // Move up the chain
                current = prevInvocation.GetInvocationReceiver(false);
            }
            else
            {
                // End of chain (variable or other source)
                break;
            }
        }

        if (foundSplitQuery) return;

        if (previousCollectionIncludes > 0)
        {
            // This is the 2nd (or later) collection include. Flag it.
            var display = propertyType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "string";
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), display));
        }
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
}
