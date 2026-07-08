using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static void ReportCandidateDiagnostics(
        CompilationAnalysisContext context,
        ConcurrentBag<DbContextCandidate> candidates,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var candidatesByType = new Dictionary<INamedTypeSymbol, List<DbContextCandidate>>(NamedTypeSymbolComparer.Instance);
        foreach (var candidate in candidates)
        {
            if (!candidatesByType.TryGetValue(candidate.ContainingType, out var typeCandidates))
            {
                typeCandidates = new List<DbContextCandidate>();
                candidatesByType.Add(candidate.ContainingType, typeCandidates);
            }

            typeCandidates.Add(candidate);
        }

        foreach (var pair in candidatesByType
                     .OrderBy(pair => GetFirstCandidatePath(pair.Value), StringComparer.Ordinal)
                     .ThenBy(pair => GetFirstCandidateStart(pair.Value))
                     .ThenBy(pair => pair.Key.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal))
        {
            if (!longLivedTypes.TryGetValue(pair.Key, out var reason))
            {
                continue;
            }

            var orderedCandidates = pair.Value
                .OrderBy(GetCandidatePath, StringComparer.Ordinal)
                .ThenBy(GetCandidateStart)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Kind)
                .ToArray();
            var storedCandidates = orderedCandidates
                .Where(candidate => candidate.Kind != CandidateKind.ConstructorParameter)
                .ToArray();
            var reportableCandidates = storedCandidates.Length > 0
                ? storedCandidates
                : orderedCandidates.Where(candidate => candidate.Kind == CandidateKind.ConstructorParameter).ToArray();

            foreach (var candidate in reportableCandidates)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    candidate.Location,
                    GetCandidateKindDisplayName(candidate.Kind),
                    candidate.Name,
                    candidate.ContainingType.Name,
                    reason));
            }
        }
    }

    private static string GetFirstCandidatePath(IEnumerable<DbContextCandidate> candidates)
    {
        return candidates
            .OrderBy(GetCandidatePath, StringComparer.Ordinal)
            .ThenBy(GetCandidateStart)
            .Select(GetCandidatePath)
            .FirstOrDefault() ?? string.Empty;
    }

    private static int GetFirstCandidateStart(IEnumerable<DbContextCandidate> candidates)
    {
        return candidates
            .OrderBy(GetCandidatePath, StringComparer.Ordinal)
            .ThenBy(GetCandidateStart)
            .Select(GetCandidateStart)
            .FirstOrDefault();
    }

    private static string GetCandidatePath(DbContextCandidate candidate)
    {
        return candidate.Location.SourceTree?.FilePath ?? string.Empty;
    }

    private static int GetCandidateStart(DbContextCandidate candidate)
    {
        return candidate.Location.IsInSource
            ? candidate.Location.SourceSpan.Start
            : int.MaxValue;
    }

    private static Location GetLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(location => location.IsInSource) ??
               symbol.Locations.FirstOrDefault() ??
               Location.None;
    }

    private static string GetCandidateKindDisplayName(CandidateKind kind)
    {
        switch (kind)
        {
            case CandidateKind.Field:
                return "field";
            case CandidateKind.Property:
                return "property";
            case CandidateKind.ConstructorParameter:
                return "constructor parameter";
            default:
                return "member";
        }
    }

    private enum CandidateKind
    {
        Field,
        Property,
        ConstructorParameter
    }

    private sealed class DbContextCandidate
    {
        public DbContextCandidate(INamedTypeSymbol containingType, string name, CandidateKind kind, Location location)
        {
            ContainingType = containingType;
            Name = name;
            Kind = kind;
            Location = location;
        }

        public INamedTypeSymbol ContainingType { get; }
        public string Name { get; }
        public CandidateKind Kind { get; }
        public Location Location { get; }
    }

    private sealed class NamedTypeSymbolComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public static readonly NamedTypeSymbolComparer Instance = new();

        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(INamedTypeSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }
}
