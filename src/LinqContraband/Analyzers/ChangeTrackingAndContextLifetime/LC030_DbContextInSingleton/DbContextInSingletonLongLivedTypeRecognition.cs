using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static bool IsKnownScopedType(INamedTypeSymbol type, Compilation compilation)
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

    private static bool HasConventionalMiddlewareSignature(INamedTypeSymbol type, Compilation compilation)
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

    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
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

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
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
