using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

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

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            ProcessConfigurationSyntax(syntax, compilationModel, null, ownedEntities, configuredForeignKeys, cancellationToken);
        }
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
