using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static void ScanEntityTypeConfigurations(
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        CancellationToken cancellationToken)
    {
        var scan = compilationModel.GetEntityTypeConfigurationScan(cancellationToken);
        configuredEntities.UnionWith(scan.ConfiguredEntities);
        keylessEntities.UnionWith(scan.KeylessEntities);
    }

    private static EntityTypeConfigurationScan BuildEntityTypeConfigurationScan(
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var scan = new EntityTypeConfigurationScan();
        var compilation = compilationModel.Compilation;
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return scan;

        foreach (var type in compilationModel.GetAllTypes(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (type.AllInterfaces.IsEmpty)
                continue;

            foreach (var iface in type.AllInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface))
                    continue;

                if (iface.TypeArguments.Length > 0 &&
                    iface.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    var (hasKey, hasNoKey) = CheckConfigureMethod(type, entityType, compilationModel, cancellationToken);

                    if (hasKey)
                        scan.ConfiguredEntities.Add(entityType);

                    if (hasNoKey)
                        scan.KeylessEntities.Add(entityType);
                }
            }
        }

        return scan;
    }

    private static (bool hasKey, bool hasNoKey) CheckConfigureMethod(
        INamedTypeSymbol configClass,
        INamedTypeSymbol entityType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var configureMethod = configClass.GetMembers("Configure").FirstOrDefault() as IMethodSymbol;
        if (configureMethod == null)
            return (false, false);

        var hasKey = false;
        var hasNoKey = false;

        foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            var builderVariables = CollectConfigureBuilderParameters(configureMethod);
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var configuredEntity) ||
                    !SymbolEqualityComparer.Default.Equals(configuredEntity, entityType))
                {
                    continue;
                }

                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "HasKey")
                    hasKey = true;
                if (methodName == "HasNoKey")
                    hasNoKey = true;
            }
        }

        return (hasKey, hasNoKey);
    }

    private static bool TryGetConfiguredEntityType(INamedTypeSymbol configType, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        foreach (var iface in configType.AllInterfaces)
        {
            if (iface.Name != "IEntityTypeConfiguration" ||
                iface.ContainingNamespace?.ToString() != "Microsoft.EntityFrameworkCore" ||
                iface.TypeArguments.Length == 0 ||
                iface.TypeArguments[0] is not INamedTypeSymbol configuredEntity)
            {
                continue;
            }

            entityType = configuredEntity;
            return true;
        }

        return false;
    }
}
