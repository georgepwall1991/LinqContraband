using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { onModelCreating };
        ScanModelConfigurationMethod(
            onModelCreating,
            null,
            compilationModel,
            ownedEntities,
            configuredForeignKeys,
            visited,
            0,
            cancellationToken);
    }

    private const int MaxModelConfigurationHelperDepth = 4;

    /// <summary>
    /// Scans a model-configuration method and the project's own helpers it calls with the model or an entity
    /// builder, such as BTCPay Server's <c>APIKeyData.OnModelCreating(builder, Database)</c> called from the
    /// context's <c>OnModelCreating</c>.
    /// </summary>
    private static void ScanModelConfigurationMethod(
        IMethodSymbol method,
        INamedTypeSymbol? configuredEntityType,
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        HashSet<IMethodSymbol> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            ProcessConfigurationSyntax(syntax, compilationModel, configuredEntityType, ownedEntities, configuredForeignKeys, cancellationToken);

            if (depth >= MaxModelConfigurationHelperDepth ||
                !compilationModel.Compilation.TryGetOwnedSemanticModel(syntax.SyntaxTree, out var semanticModel))
            {
                continue;
            }

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol called)
                    continue;

                var helper = called.ReducedFrom ?? called;
                helper = helper.OriginalDefinition;
                if (helper.DeclaringSyntaxReferences.IsEmpty ||
                    !TryGetConfiguredEntityParameter(helper, out var helperEntityType) ||
                    !visited.Add(helper))
                {
                    continue;
                }

                ScanModelConfigurationMethod(
                    helper,
                    helperEntityType,
                    compilationModel,
                    ownedEntities,
                    configuredForeignKeys,
                    visited,
                    depth + 1,
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// A helper configures the model when it takes a <c>ModelBuilder</c> (entities come from its
    /// <c>Entity&lt;T&gt;()</c> calls) or an <c>EntityTypeBuilder&lt;T&gt;</c> (the entity is <c>T</c>).
    /// </summary>
    private static bool TryGetConfiguredEntityParameter(IMethodSymbol method, out INamedTypeSymbol? entityType)
    {
        entityType = null;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol type ||
                type.ContainingNamespace?.ToDisplayString() is not ("Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders"))
            {
                continue;
            }

            if (type.Name == "ModelBuilder")
                return true;

            if (type.Name == "EntityTypeBuilder" && type.TypeArguments.Length == 1)
            {
                entityType = type.TypeArguments[0] as INamedTypeSymbol;
                return true;
            }
        }

        return false;
    }

    private static void ScanEntityTypeConfigurations(
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
        var scan = compilationModel.GetConfigurationScan(cancellationToken);
        ownedEntities.UnionWith(scan.OwnedEntities);
        configuredForeignKeys.UnionWith(scan.ConfiguredForeignKeys);
    }

    private static ConfigurationScan BuildConfigurationScan(
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var scan = new ConfigurationScan();
        var compilation = compilationModel.Compilation;
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return scan;

        foreach (var type in compilationModel.GetAllTypes(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var iface in type.AllInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface) ||
                    iface.TypeArguments.Length == 0 ||
                    iface.TypeArguments[0] is not INamedTypeSymbol entityType)
                {
                    continue;
                }

                var configureMethod = type.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
                if (configureMethod == null)
                    continue;

                foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var syntax = syntaxRef.GetSyntax(cancellationToken);
                    ProcessConfigurationSyntax(
                        syntax,
                        compilationModel,
                        entityType,
                        scan.OwnedEntities,
                        scan.ConfiguredForeignKeys,
                        cancellationToken);
                }
            }
        }

        return scan;
    }

}
