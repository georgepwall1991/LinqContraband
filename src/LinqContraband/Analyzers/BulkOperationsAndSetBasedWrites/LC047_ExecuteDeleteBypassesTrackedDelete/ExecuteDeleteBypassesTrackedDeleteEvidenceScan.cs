using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private void EnsureScanned(CancellationToken cancellationToken)
    {
        if (scanned)
            return;

        lock (gate)
        {
            if (scanned)
                return;

            Scan(cancellationToken);
            scanned = true;
        }
    }

    private void Scan(CancellationToken cancellationToken)
    {
        var interceptorConversions = new Dictionary<INamedTypeSymbol, ConversionScan>(SymbolEqualityComparer.Default);

        foreach (var type in EnumerateSourceTypes(compilation.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsFrameworkDbContext(type))
                continue;

            if (type.IsDbContext())
            {
                ScanContextType(type, cancellationToken);
                continue;
            }

            if (IsSaveChangesInterceptor(type))
            {
                var conversion = ScanTypeMethods(type, isInterceptor: true, cancellationToken);
                if (conversion.IsConversion)
                    interceptorConversions[type] = conversion;
            }
        }

        foreach (var type in EnumerateSourceTypes(compilation.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!type.IsDbContext() || IsFrameworkDbContext(type))
                continue;

            foreach (var interceptorType in GetRegisteredInterceptorTypes(type, cancellationToken))
            {
                if (!TryGetInterceptorConversion(interceptorType, interceptorConversions, out var conversion))
                    continue;

                ApplyConversion(type, conversion);
            }
        }
    }

    private void ScanContextType(INamedTypeSymbol contextType, CancellationToken cancellationToken)
    {
        var conversion = ScanTypeMethods(contextType, isInterceptor: false, cancellationToken);
        if (conversion.IsConversion)
            ApplyConversion(contextType, conversion);

        ScanOnModelCreatingCascades(contextType, cancellationToken);
    }

    private void ApplyConversion(INamedTypeSymbol contextType, ConversionScan conversion)
    {
        var canonicalContext = CanonicalContext(contextType);
        if (conversion.EntityTypes.Count == 0)
        {
            contextWideConversions.Add(canonicalContext);
            if (conversion.SingleBoolTrueProperty != null)
                contextWideProperties[canonicalContext] = conversion.SingleBoolTrueProperty;
            return;
        }

        foreach (var entity in conversion.EntityTypes)
        {
            entityConversions.Add(new TypePair(canonicalContext, entity));
            if (conversion.SingleBoolTrueProperty != null)
                conversionProperties[new TypePair(canonicalContext, entity)] = conversion.SingleBoolTrueProperty;
        }
    }

    private static bool TryGetInterceptorConversion(
        INamedTypeSymbol interceptorType,
        Dictionary<INamedTypeSymbol, ConversionScan> interceptorConversions,
        out ConversionScan conversion)
    {
        for (var current = interceptorType; current != null; current = current.BaseType)
        {
            if (interceptorConversions.TryGetValue(current, out conversion))
                return true;
        }

        conversion = default;
        return false;
    }

    private static bool IsFrameworkDbContext(INamedTypeSymbol type)
    {
        return type.Name == "DbContext" &&
               type.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }

    private static bool IsSaveChangesInterceptor(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name == "SaveChangesInterceptor" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore.Diagnostics")
            {
                return true;
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == "ISaveChangesInterceptor" &&
                iface.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore.Diagnostics")
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(
        INamespaceSymbol namespaceSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var sourceType in EnumerateSourceTypeAndNested(type, cancellationToken))
                yield return sourceType;
        }

        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in EnumerateSourceTypes(child, cancellationToken))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypeAndNested(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        if (HasSourceLocation(type))
            yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var child in EnumerateSourceTypeAndNested(nested, cancellationToken))
                yield return child;
        }
    }

    private static bool HasSourceLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
                return true;
        }

        return false;
    }
}
