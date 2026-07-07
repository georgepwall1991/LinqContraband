using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

internal static class UseFrozenSetForStaticMembershipCachesDiagnosticProperties
{
    public const string FixerEligible = "LC033.FixerEligible";
}

internal enum FrozenSetInitializerKind
{
    CollectionInitializer,
    SourceConstructor,
    ToHashSetInvocation
}

internal readonly struct FrozenSetSupport
{
    public FrozenSetSupport(
        INamedTypeSymbol hashSetType,
        INamedTypeSymbol frozenSetType,
        INamedTypeSymbol expressionType,
        ImmutableArray<IMethodSymbol> toFrozenSetMethods)
    {
        HashSetType = hashSetType;
        FrozenSetType = frozenSetType;
        ExpressionType = expressionType;
        ToFrozenSetMethods = toFrozenSetMethods;
    }

    public INamedTypeSymbol HashSetType { get; }
    public INamedTypeSymbol FrozenSetType { get; }
    public INamedTypeSymbol ExpressionType { get; }
    public ImmutableArray<IMethodSymbol> ToFrozenSetMethods { get; }
}

internal static partial class UseFrozenSetForStaticMembershipCachesAnalysis
{
    public static bool TryGetFrozenSetSupport(Compilation compilation, out FrozenSetSupport support)
    {
        support = default;

        var hashSetType = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1");
        var frozenSetType = compilation.GetTypeByMetadataName("System.Collections.Frozen.FrozenSet`1");
        var expressionType = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");

        if (hashSetType is null || frozenSetType is null || expressionType is null)
            return false;

        var toFrozenSetMethods = compilation
            .GetSymbolsWithName(static name => name == "ToFrozenSet", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Where(static method => method.IsExtensionMethod)
            .Where(method => method.ContainingNamespace?.ToDisplayString() == "System.Collections.Frozen")
            .Where(method => method.ReturnType is INamedTypeSymbol returnType &&
                             SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, frozenSetType))
            .ToImmutableArray();

        if (toFrozenSetMethods.IsDefaultOrEmpty)
            return false;

        support = new FrozenSetSupport(hashSetType, frozenSetType, expressionType, toFrozenSetMethods);
        return true;
    }

    public static bool IsHashSetType(ITypeSymbol? type, INamedTypeSymbol hashSetType)
    {
        return type is INamedTypeSymbol namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, hashSetType);
    }

    public static bool IsExpressionType(ITypeSymbol? type, INamedTypeSymbol expressionType)
    {
        return type is INamedTypeSymbol namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, expressionType);
    }
}
