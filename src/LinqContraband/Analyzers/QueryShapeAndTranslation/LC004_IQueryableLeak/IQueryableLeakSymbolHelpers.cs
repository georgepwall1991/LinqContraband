using System.Linq;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private bool IsEnumerableMethod(IMethodSymbol method)
    {
        return _linqEnumerableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqEnumerableType);
    }

    private bool IsQueryableMethod(IMethodSymbol method)
    {
        return _linqQueryableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqQueryableType);
    }

    private bool IsIEnumerableParameterType(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _enumerableType) ||
               TryGetConstructedInterface(type, _enumerableGenericType, out var enumerableInterface) &&
               SymbolEqualityComparer.Default.Equals(type, enumerableInterface);
    }

    private bool IsIQueryableType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return SymbolEqualityComparer.Default.Equals(type, _queryableType) ||
               TryGetConstructedInterface(type, _queryableGenericType, out _) ||
               type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _queryableType));
    }

    private bool IsIEnumerableLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(type, _enumerableType))
            return true;

        return TryGetConstructedInterface(type, _enumerableGenericType, out _) ||
               type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _enumerableType));
    }

    private bool TryGetConstructedInterface(ITypeSymbol? type, INamedTypeSymbol? interfaceType, out INamedTypeSymbol match)
    {
        match = null!;
        if (type == null || interfaceType == null)
            return false;

        if (type is INamedTypeSymbol namedType &&
            SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, interfaceType))
        {
            match = namedType;
            return true;
        }

        foreach (var currentInterface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(currentInterface.OriginalDefinition, interfaceType))
            {
                match = currentInterface;
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol GetOriginalTargetMethod(IMethodSymbol method)
    {
        return method.ReducedFrom ?? method;
    }
}
