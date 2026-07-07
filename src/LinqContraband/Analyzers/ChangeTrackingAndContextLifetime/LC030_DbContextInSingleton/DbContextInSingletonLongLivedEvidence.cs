using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC030_DbContextInSingleton;

public sealed partial class DbContextInSingletonAnalyzer
{
    private static void AddIntrinsicLongLivedEvidence(
        INamedTypeSymbol type,
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider,
        SyntaxTree? syntaxTree,
        ConcurrentDictionary<INamedTypeSymbol, string> longLivedTypes)
    {
        var options = GetOptions(optionsProvider, syntaxTree);
        if (TryGetConfiguredLongLivedReason(type, options, out var configuredReason))
        {
            longLivedTypes.TryAdd(type, configuredReason);
            return;
        }

        if (IsKnownScopedType(type, compilation))
        {
            return;
        }

        var hostedServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");
        if (hostedServiceType != null && ImplementsInterface(type, hostedServiceType))
        {
            longLivedTypes.TryAdd(type, "implements IHostedService");
            return;
        }

        var backgroundServiceType = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.BackgroundService");
        if (backgroundServiceType != null && InheritsFrom(type, backgroundServiceType))
        {
            longLivedTypes.TryAdd(type, "inherits BackgroundService");
            return;
        }

        if (HasConventionalMiddlewareSignature(type, compilation))
        {
            longLivedTypes.TryAdd(type, "has a conventional middleware Invoke/InvokeAsync signature");
            return;
        }

        if (options.ExpandedDetection && HasExpandedLongLivedName(type))
        {
            longLivedTypes.TryAdd(type, "matches expanded long-lived naming heuristics");
        }
    }

    private static bool HasExpandedLongLivedName(INamedTypeSymbol type)
    {
        return type.Name.IndexOf("Singleton", StringComparison.Ordinal) >= 0 ||
               type.Name.EndsWith("HostedService", StringComparison.Ordinal) ||
               type.Name.EndsWith("BackgroundWorker", StringComparison.Ordinal);
    }

}
