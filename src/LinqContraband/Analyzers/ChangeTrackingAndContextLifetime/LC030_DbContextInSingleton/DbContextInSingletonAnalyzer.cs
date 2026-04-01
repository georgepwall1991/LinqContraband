using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

/// <summary>
/// Analyzes class members to detect DbContext instances held in potentially singleton or long-lived services. Diagnostic ID: LC030
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DbContextInSingletonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC030";
    private const string Category = "Architecture";
    private static readonly LocalizableString Title = "Potential DbContext lifetime mismatch";

    private static readonly LocalizableString MessageFormat =
        "The class '{0}' stores a 'DbContext' in member '{1}'. This is safe for scoped types, but risky for long-lived services. Review the lifetime before keeping it as a field or property.";

    private static readonly LocalizableString Description =
        "DbContext is not thread-safe and is intended to be short-lived. Storing it on a long-lived type can be risky, so review the service lifetime and prefer factories when the lifetime is uncertain.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC030_DbContextInSingleton.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsStatic) return;

        if (field.Type.IsDbContext())
        {
            CheckContainingType(context, field.ContainingType, field.Name, field.Locations[0]);
        }
    }

    private void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.IsStatic) return;

        if (property.Type.IsDbContext())
        {
            CheckContainingType(context, property.ContainingType, property.Name, property.Locations[0]);
        }
    }

    private void CheckContainingType(SymbolAnalysisContext context, INamedTypeSymbol type, string memberName, Location location)
    {
        // Skip DbContext itself
        if (type.IsDbContext()) return;

        // Skip known scoped or per-request types
        if (IsKnownScopedType(type, context.Compilation)) return;

        if (!IsLikelyLongLivedType(type, context.Compilation)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, memberName));
    }

    private bool IsKnownScopedType(INamedTypeSymbol type, Compilation compilation)
    {
        var current = type;
        while (current != null)
        {
            var name = current.Name;

            // ASP.NET Core Controllers
            if (name.EndsWith("Controller", System.StringComparison.Ordinal) ||
                name.EndsWith("ViewComponent", System.StringComparison.Ordinal) ||
                name.EndsWith("PageModel", System.StringComparison.Ordinal))
                return true;

            current = current.BaseType;
        }

        var middlewareType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IMiddleware");
        if (middlewareType != null && ImplementsInterface(type, middlewareType))
        {
            return true;
        }

        return false;
    }

    private bool IsLikelyLongLivedType(INamedTypeSymbol type, Compilation compilation)
    {
        var hostedServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");
        if (hostedServiceType != null && ImplementsInterface(type, hostedServiceType))
        {
            return true;
        }

        var backgroundServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.BackgroundService");
        if (backgroundServiceType != null && InheritsFrom(type, backgroundServiceType))
        {
            return true;
        }

        return HasConventionalMiddlewareSignature(type, compilation);
    }

    private bool HasConventionalMiddlewareSignature(INamedTypeSymbol type, Compilation compilation)
    {
        var httpContextType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.HttpContext");
        if (httpContextType == null)
        {
            return false;
        }

        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Name is not ("Invoke" or "InvokeAsync"))
            {
                continue;
            }

            if (method.IsStatic || method.Parameters.Length == 0)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, httpContextType))
            {
                return true;
            }
        }

        return false;
    }

    private bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
    {
        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedInterface.OriginalDefinition, interfaceType) ||
                SymbolEqualityComparer.Default.Equals(implementedInterface, interfaceType))
            {
                return true;
            }
        }

        return false;
    }

    private bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType) ||
                SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
