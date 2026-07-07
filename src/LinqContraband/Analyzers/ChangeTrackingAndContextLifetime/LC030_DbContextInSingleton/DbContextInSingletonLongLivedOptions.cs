using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static Lc030Options GetOptions(AnalyzerConfigOptionsProvider provider, SyntaxTree? syntaxTree)
    {
        var expandedDetection = false;
        var longLivedTypes = new HashSet<string>(StringComparer.Ordinal);

        if (syntaxTree == null)
        {
            return new Lc030Options(expandedDetection, longLivedTypes);
        }

        var options = provider.GetOptions(syntaxTree);
        if (options.TryGetValue(DetectionModeKey, out var detectionMode) &&
            string.Equals(detectionMode.Trim(), "expanded", StringComparison.OrdinalIgnoreCase))
        {
            expandedDetection = true;
        }

        if (options.TryGetValue(LongLivedTypesKey, out var configuredTypes))
        {
            foreach (var configuredType in configuredTypes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = configuredType.Trim();
                if (trimmed.Length > 0)
                {
                    longLivedTypes.Add(trimmed);
                }
            }
        }

        return new Lc030Options(expandedDetection, longLivedTypes);
    }

    private static bool TryGetConfiguredLongLivedReason(INamedTypeSymbol type, Lc030Options options, out string reason)
    {
        reason = string.Empty;
        if (options.LongLivedTypes.Count == 0)
        {
            return false;
        }

        var current = type;
        while (current != null)
        {
            if (MatchesConfiguredName(current, options.LongLivedTypes))
            {
                reason = "matches configured long-lived type '" + GetDisplayName(current) + "'";
                return true;
            }

            current = current.BaseType;
        }

        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (MatchesConfiguredName(implementedInterface, options.LongLivedTypes))
            {
                reason = "matches configured long-lived type '" + GetDisplayName(implementedInterface) + "'";
                return true;
            }
        }

        return false;
    }

    private static bool MatchesConfiguredName(INamedTypeSymbol type, HashSet<string> configuredNames)
    {
        return configuredNames.Contains(GetDisplayName(type)) ||
               configuredNames.Contains(GetDisplayName(type.OriginalDefinition));
    }

    private static string GetDisplayName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private sealed class Lc030Options
    {
        public Lc030Options(bool expandedDetection, HashSet<string> longLivedTypes)
        {
            ExpandedDetection = expandedDetection;
            LongLivedTypes = longLivedTypes;
        }

        public bool ExpandedDetection { get; }
        public HashSet<string> LongLivedTypes { get; }
    }
}
