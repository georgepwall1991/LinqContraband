using System.Collections.Concurrent;
using LinqContraband.Extensions;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static void AnalyzeField(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var field = (IFieldSymbol)context.Symbol;

        if (field.Type.IsDbContext())
        {
            AddCandidate(context, candidates, longLivedTypes, field.ContainingType, field.Name, CandidateKind.Field, GetLocation(field));
        }
    }

    private static void AnalyzeProperty(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;

        // A partial property's defining declaration has no backing storage; the implementing
        // declaration decides. It is visited by this same action, so skipping the definition
        // avoids reporting a computed partial property as if it stored a context.
        if (IsPartialDefiningDeclaration(propertyDeclaration))
        {
            return;
        }

        var property = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration, context.CancellationToken) as IPropertySymbol;

        if (property?.Type.IsDbContext() == true &&
            !IsFreshComputedProperty(property, propertyDeclaration, context.SemanticModel, context.CancellationToken))
        {
            AddCandidate(
                context,
                candidates,
                longLivedTypes,
                property.ContainingType,
                property.Name,
                CandidateKind.Property,
                GetLocation(property));
        }
    }

    private static void AnalyzeConstructor(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Constructor || method.IsStatic) return;

        foreach (var parameter in method.Parameters)
        {
            if (!parameter.Type.IsDbContext())
            {
                continue;
            }

            AddCandidate(
                context,
                candidates,
                longLivedTypes,
                method.ContainingType,
                parameter.Name,
                CandidateKind.ConstructorParameter,
                GetLocation(parameter));
        }
    }

    private static void AddCandidate(
        SymbolAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes,
        INamedTypeSymbol containingType,
        string name,
        CandidateKind kind,
        Location location)
    {
        AddCandidate(
            candidates,
            longLivedTypes,
            containingType,
            name,
            kind,
            location,
            context.Compilation,
            context.Options.AnalyzerConfigOptionsProvider);
    }

    private static void AddCandidate(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes,
        INamedTypeSymbol containingType,
        string name,
        CandidateKind kind,
        Location location)
    {
        AddCandidate(
            candidates,
            longLivedTypes,
            containingType,
            name,
            kind,
            location,
            context.SemanticModel.Compilation,
            context.Options.AnalyzerConfigOptionsProvider);
    }

    private static void AddCandidate(
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes,
        INamedTypeSymbol containingType,
        string name,
        CandidateKind kind,
        Location location,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (containingType.TypeKind != TypeKind.Class) return;
        if (containingType.IsDbContext()) return;

        candidates.Add(new DbContextCandidate(containingType, name, kind, location));
        AddIntrinsicLongLivedEvidence(
            containingType,
            compilation,
            optionsProvider,
            location.SourceTree,
            longLivedTypes);
    }

    private static bool IsPartialDefiningDeclaration(PropertyDeclarationSyntax declaration)
    {
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return false;
        }

        if (declaration.ExpressionBody != null)
        {
            return false;
        }

        var accessors = declaration.AccessorList?.Accessors;
        if (accessors == null)
        {
            return false;
        }

        // The implementing declaration must supply a body; the defining one never does.
        return accessors.Value.All(accessor => accessor.Body == null && accessor.ExpressionBody == null);
    }
}
