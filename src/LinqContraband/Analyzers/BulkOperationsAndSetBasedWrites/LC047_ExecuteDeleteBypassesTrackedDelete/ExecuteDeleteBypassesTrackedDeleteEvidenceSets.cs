using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private readonly HashSet<INamedTypeSymbol> contextWideConversions = new(SymbolEqualityComparer.Default);
    private readonly HashSet<TypePair> entityConversions = new();
    private readonly HashSet<TypePair> clientCascadePrincipals = new();
    private readonly Dictionary<TypePair, string> conversionProperties = new();
    private readonly Dictionary<INamedTypeSymbol, string> contextWideProperties = new(SymbolEqualityComparer.Default);

    private bool HasEntityConversion(INamedTypeSymbol contextType, ITypeSymbol entityType)
    {
        foreach (var pair in entityConversions)
        {
            if (!SymbolEqualityComparer.Default.Equals(pair.Left, contextType))
                continue;

            if (EntityMatchesRegistered(entityType, pair.Right))
                return true;
        }

        return false;
    }

    private bool HasClientCascade(INamedTypeSymbol contextType, ITypeSymbol entityType)
    {
        foreach (var pair in clientCascadePrincipals)
        {
            if (!SymbolEqualityComparer.Default.Equals(pair.Left, contextType))
                continue;

            if (EntityMatchesRegistered(entityType, pair.Right))
                return true;
        }

        return false;
    }

    private bool TryGetProperty(INamedTypeSymbol contextType, ITypeSymbol entityType, out string propertyName)
    {
        if (contextWideConversions.Contains(contextType) &&
            contextWideProperties.TryGetValue(contextType, out propertyName!))
        {
            return true;
        }

        foreach (var pair in conversionProperties)
        {
            if (!SymbolEqualityComparer.Default.Equals(pair.Key.Left, contextType))
                continue;

            if (!EntityMatchesRegistered(entityType, pair.Key.Right))
                continue;

            propertyName = pair.Value;
            return true;
        }

        propertyName = null!;
        return false;
    }

    private static bool EntityMatchesRegistered(ITypeSymbol entityType, ITypeSymbol registered)
    {
        if (SymbolEqualityComparer.Default.Equals(entityType, registered))
            return true;

        for (var current = entityType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, registered))
                return true;
        }

        foreach (var iface in entityType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, registered))
                return true;
        }

        return false;
    }

    internal readonly struct TypePair : System.IEquatable<TypePair>
    {
        public TypePair(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            Left = left;
            Right = right;
        }

        public INamedTypeSymbol Left { get; }
        public INamedTypeSymbol Right { get; }

        public bool Equals(TypePair other)
        {
            return SymbolEqualityComparer.Default.Equals(Left, other.Left) &&
                   SymbolEqualityComparer.Default.Equals(Right, other.Right);
        }

        public override bool Equals(object? obj) => obj is TypePair other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (SymbolEqualityComparer.Default.GetHashCode(Left) * 397) ^
                       SymbolEqualityComparer.Default.GetHashCode(Right);
            }
        }
    }
}
