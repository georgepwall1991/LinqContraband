using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// The concrete classes and structs this compilation declares, keyed by each interface they implement and each
/// base class they derive from. It answers "which bodies can this interface or virtual call reach in this
/// project", and is built once per compilation, the first time a loop makes such a call.
/// </summary>
internal sealed class NPlusOneLooperSourceTypeIndex
{
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> implementers =
        new(SymbolEqualityComparer.Default);

    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> derived =
        new(SymbolEqualityComparer.Default);

    private readonly Dictionary<ITypeSymbol, List<INamedTypeSymbol>> handlers =
        new(SymbolEqualityComparer.Default);

    public NPlusOneLooperSourceTypeIndex(Compilation compilation, CancellationToken cancellationToken)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is INamespaceSymbol @namespace)
                {
                    pending.Push(@namespace);
                }
                else if (member is INamedTypeSymbol type)
                {
                    pending.Push(type);
                    Add(type);
                }
            }
        }
    }

    /// <summary>Concrete source types implementing the interface, in declaration order.</summary>
    public IReadOnlyList<INamedTypeSymbol> GetImplementers(INamedTypeSymbol @interface)
    {
        return implementers.TryGetValue(@interface.OriginalDefinition, out var types)
            ? types
            : ImmutableArray<INamedTypeSymbol>.Empty;
    }

    /// <summary>Concrete source types deriving, at any distance, from the class.</summary>
    public IReadOnlyList<INamedTypeSymbol> GetDerived(INamedTypeSymbol @class)
    {
        return derived.TryGetValue(@class.OriginalDefinition, out var types)
            ? types
            : ImmutableArray<INamedTypeSymbol>.Empty;
    }

    /// <summary>
    /// Concrete source types implementing a generic interface whose name contains <c>Handler</c> with the request
    /// type as its first type argument (<c>IRequestHandler&lt;GetCustomer, Customer&gt;</c>), in declaration order.
    /// </summary>
    public IReadOnlyList<INamedTypeSymbol> GetHandlers(ITypeSymbol requestType)
    {
        return handlers.TryGetValue(requestType, out var types)
            ? types
            : ImmutableArray<INamedTypeSymbol>.Empty;
    }

    internal static bool IsHandlerInterfaceFor(INamedTypeSymbol @interface, ITypeSymbol? requestType)
    {
        return @interface.TypeArguments.Length > 0 &&
               @interface.Name.Contains("Handler") &&
               (requestType == null ||
                SymbolEqualityComparer.Default.Equals(@interface.TypeArguments[0], requestType));
    }

    private void Add(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract)
            return;

        foreach (var @interface in type.AllInterfaces)
        {
            AddTo(implementers, @interface.OriginalDefinition, type);
            if (IsHandlerInterfaceFor(@interface, requestType: null) &&
                @interface.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } request)
            {
                AddTo(handlers, request, type);
            }
        }

        for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            AddTo(derived, baseType.OriginalDefinition, type);
    }

    private static void AddTo<TKey>(
        Dictionary<TKey, List<INamedTypeSymbol>> map,
        TKey key,
        INamedTypeSymbol type)
    {
        if (!map.TryGetValue(key, out var types))
            map[key] = types = new List<INamedTypeSymbol>();

        // A type lists a generic interface once per construction (IHandler<A>, IHandler<B>).
        if (types.Count == 0 || !SymbolEqualityComparer.Default.Equals(types[types.Count - 1], type))
            types.Add(type);
    }
}
