using System.Linq;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Extensions;

/// <summary>
/// Finds the namespace that declares an <c>IQueryable</c> extension method a code fix is about to emit,
/// so the fix can import it. EF Core's own extension classes are checked first; source declarations
/// (shims and test doubles) are the fallback.
/// </summary>
internal static class QueryableExtensionNamespaceResolver
{
    private static readonly string[] EfCoreExtensionTypes =
    {
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
    };

    public static string? Resolve(Compilation compilation, string methodName)
    {
        foreach (var typeName in EfCoreExtensionTypes)
        {
            var method = compilation.GetTypeByMetadataName(typeName)?
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(IsQueryableExtension);

            if (method is not null)
                return NamespaceName(method);
        }

        var declared = compilation.GetSymbolsWithName(methodName, SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsQueryableExtension);

        return declared is null ? null : NamespaceName(declared);
    }

    private static string? NamespaceName(IMethodSymbol method) =>
        method.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToString()
            : null;

    public static bool IsQueryableExtension(IMethodSymbol method) =>
        method.IsExtensionMethod && method.Parameters.Length > 0 && method.Parameters[0].Type.IsIQueryable();
}
