using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

/// <summary>
/// Compilation-wide proof that a DbContext converts tracked deletes or uses client cascade.
/// Shared with LC012 so RemoveRange is not rewritten into a hard DELETE on those models.
/// </summary>
internal sealed partial class TrackedDeletePipelineEvidence
{
    private static readonly ConditionalWeakTable<Compilation, TrackedDeletePipelineEvidence> Cache = new();

    private readonly Compilation compilation;
    private readonly object gate = new();
    private readonly INamedTypeSymbol? entityTypeConfigurationInterface;
    private bool scanned;

    private TrackedDeletePipelineEvidence(Compilation compilation)
    {
        this.compilation = compilation;
        entityTypeConfigurationInterface = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
    }

    public static TrackedDeletePipelineEvidence Get(Compilation compilation, CancellationToken cancellationToken)
    {
        TrackedDeletePipelineEvidence evidence;
#if NETSTANDARD2_0 || NETSTANDARD2_1
        if (!Cache.TryGetValue(compilation, out evidence))
        {
            evidence = new TrackedDeletePipelineEvidence(compilation);
            try
            {
                Cache.Add(compilation, evidence);
            }
            catch (System.ArgumentException)
            {
                if (!Cache.TryGetValue(compilation, out var raced))
                    evidence = raced ?? evidence;
                else
                    evidence = raced;
            }
        }
#else
        evidence = Cache.GetValue(compilation, static c => new TrackedDeletePipelineEvidence(c));
#endif
        evidence.EnsureScanned(cancellationToken);
        return evidence;
    }

    public bool IsCovered(ITypeSymbol? contextType, ITypeSymbol? entityType)
    {
        if (contextType == null || entityType == null)
            return false;

        foreach (var candidate in EnumerateContextTypes(contextType))
        {
            if (contextWideConversions.Contains(candidate))
                return true;

            if (HasEntityConversion(candidate, entityType))
                return true;

            if (HasClientCascade(candidate, entityType))
                return true;
        }

        return false;
    }

    public bool TryGetSingleBoolTrueConversionProperty(
        ITypeSymbol? contextType,
        ITypeSymbol? entityType,
        out string propertyName)
    {
        propertyName = null!;
        if (contextType == null || entityType == null)
            return false;

        string? found = null;
        foreach (var candidate in EnumerateContextTypes(contextType))
        {
            if (!TryGetProperty(candidate, entityType, out var name))
                continue;

            if (found != null && found != name)
                return false;

            found = name;
        }

        if (found == null)
            return false;

        propertyName = found;
        return true;
    }

    public bool IsEntityCoveredOnAnyContext(ITypeSymbol entityType)
    {
        if (contextWideConversions.Count > 0)
            return true;

        foreach (var pair in entityConversions)
        {
            if (EntityMatchesRegistered(entityType, pair.Right))
                return true;
        }

        foreach (var pair in clientCascadePrincipals)
        {
            if (EntityMatchesRegistered(entityType, pair.Right))
                return true;
        }

        return false;
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateContextTypes(ITypeSymbol contextType)
    {
        for (var current = contextType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                yield break;
            }

            yield return CanonicalContext(current);
        }
    }

    private static INamedTypeSymbol CanonicalContext(INamedTypeSymbol type) => type.OriginalDefinition;
}
