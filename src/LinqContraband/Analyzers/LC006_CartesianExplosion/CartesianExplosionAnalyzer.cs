using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CartesianExplosionAnalyzer : DiagnosticAnalyzer
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

        // Check if THIS Include is a collection include
        // Include<T, TProperty>(...)
        if (method.TypeArguments.Length < 2) return;
        var propertyType = method.TypeArguments[1];

        if (!IsCollection(propertyType)) return;

        // Now check the chain backwards. Handle extension method syntax.
        var current = invocation.Instance ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);
        
        bool foundSplitQuery = false;
        int previousCollectionIncludes = 0;

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
                    if (prevMethod.TypeArguments.Length >= 2)
                    {
                        var prevPropType = prevMethod.TypeArguments[1];
                        if (IsCollection(prevPropType))
                        {
                            previousCollectionIncludes++;
                        }
                    }
                }

                // Move up the chain
                current = prevInvocation.Instance ?? (prevInvocation.Arguments.Length > 0 ? prevInvocation.Arguments[0].Value : null);
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
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), propertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private bool IsCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return false;
        // Arrays are collections
        if (type.TypeKind == TypeKind.Array) return true;

        if (type is INamedTypeSymbol namedType)
        {
            if (namedType.Name == "List" && namedType.IsGenericType) return true;
            if (namedType.Name == "IList" && namedType.IsGenericType) return true;
            if (namedType.Name == "IEnumerable" && namedType.IsGenericType) return true;
            if (namedType.Name == "ICollection" && namedType.IsGenericType) return true;
            if (namedType.Name == "HashSet" && namedType.IsGenericType) return true;
            if (namedType.Name == "ISet" && namedType.IsGenericType) return true;
        }
        
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == "IEnumerable" && iface.IsGenericType && 
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic")
                return true;
        }

        return false;
    }
}