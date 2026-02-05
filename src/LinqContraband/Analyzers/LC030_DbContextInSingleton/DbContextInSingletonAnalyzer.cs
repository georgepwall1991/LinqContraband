using System.Collections.Immutable;
using System.Linq;
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
        "The class '{0}' holds a 'DbContext' in field '{1}'. Ensure this class is registered with a Scoped lifetime, not Singleton, to avoid threading and memory issues.";

    private static readonly LocalizableString Description =
        "DbContext is not thread-safe and should be short-lived. Holding it in a Singleton service leads to crashes and memory leaks.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC030_DbContextInSingleton.md");

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

        // Skip known Scoped types
        if (IsKnownScopedType(type)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, memberName));
    }

    private bool IsKnownScopedType(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            var name = current.Name;
            var ns = current.ContainingNamespace?.ToString();

            // ASP.NET Core Controllers
            if (name.EndsWith("Controller", System.StringComparison.Ordinal) ||
                name.EndsWith("ViewComponent", System.StringComparison.Ordinal) ||
                name.EndsWith("PageModel", System.StringComparison.Ordinal))
                return true;

            // Middleware usually takes DB in Invoke, but if it has a field, it's actually a SINGLETON risk!
            // So we DON'T skip Middleware fields.

            current = current.BaseType;
        }

        return false;
    }
}
