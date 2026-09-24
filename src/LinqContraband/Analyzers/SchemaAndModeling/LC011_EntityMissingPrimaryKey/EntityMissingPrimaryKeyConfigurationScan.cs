using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        var visitedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        ScanModelConfigurationMethod(
            onModelCreating,
            dbContextType,
            configuredEntities,
            keylessEntities,
            ownedEntities,
            compilationModel,
            visitedMethods,
            depth: 0,
            cancellationToken);
    }

    // OnModelCreating often hands the ModelBuilder to helpers, such as a static
    // `AddressData.OnModelCreating(builder)` on each entity or a `builder.ConfigureUsers()`
    // extension, or passes `builder.Entity<User>()` to one, so source methods that take a
    // ModelBuilder or an EntityTypeBuilder<T> are scanned as part of it.
    private const int MaxModelConfigurationHelperDepth = 4;

    private static void ScanModelConfigurationMethod(
        IMethodSymbol method,
        INamedTypeSymbol dbContextType,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        HashSet<INamedTypeSymbol> ownedEntities,
        CompilationModel compilationModel,
        HashSet<IMethodSymbol> visitedMethods,
        int depth,
        CancellationToken cancellationToken)
    {
        if (!visitedMethods.Add(method.OriginalDefinition))
            return;

        foreach (var syntaxRef in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            var builderVariables = CollectConfigureBuilderParameters(method);
            SemanticModel? semanticModel = null;
            var semanticModelResolved = false;

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var methodName = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
                    IdentifierNameSyntax identifier => identifier.Identifier.Text,
                    GenericNameSyntax generic => generic.Identifier.Text,
                    _ => null
                };
                if (methodName == null)
                    continue;

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    if (methodName == "HasKey" &&
                        TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var configuredEntity))
                    {
                        configuredEntities.Add(configuredEntity);
                        continue;
                    }

                    if (methodName == "HasNoKey" &&
                        TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var keylessEntity))
                    {
                        keylessEntities.Add(keylessEntity);
                        continue;
                    }

                    if (methodName is "OwnsOne" or "OwnsMany" &&
                        TryGetOwnedEntityType(invocation, memberAccess, builderVariables, compilationModel, cancellationToken, out var ownedEntity))
                    {
                        ownedEntities.Add(ownedEntity);
                        continue;
                    }

                    if (methodName == "ApplyConfiguration")
                    {
                        ScanAppliedConfiguration(invocation, dbContextType, compilationModel, configuredEntities, keylessEntities, cancellationToken);
                        continue;
                    }

                    if (methodName == "ApplyConfigurationsFromAssembly")
                    {
                        if (ShouldScanCurrentAssemblyConfigurations(invocation, dbContextType, compilationModel, cancellationToken))
                            ScanEntityTypeConfigurations(compilationModel, configuredEntities, keylessEntities, cancellationToken);
                        continue;
                    }
                }

                if (depth >= MaxModelConfigurationHelperDepth || methodName is "Entity" or "HasOne" or "HasMany" or "WithOne" or "WithMany" or "Property" or "HasIndex")
                    continue;

                if (!semanticModelResolved)
                {
                    semanticModelResolved = true;
                    if (compilationModel.Compilation.TryGetOwnedSemanticModel(syntax.SyntaxTree, out var model))
                        semanticModel = model;
                }

                if (semanticModel?.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol helper &&
                    TryGetModelConfigurationHelper(helper, out var helperDefinition))
                {
                    ScanModelConfigurationMethod(
                        helperDefinition,
                        dbContextType,
                        configuredEntities,
                        keylessEntities,
                        ownedEntities,
                        compilationModel,
                        visitedMethods,
                        depth + 1,
                        cancellationToken);
                }
            }
        }
    }

    private static bool TryGetModelConfigurationHelper(IMethodSymbol method, out IMethodSymbol helper)
    {
        helper = (method.ReducedFrom ?? method).OriginalDefinition;
        if (helper.DeclaringSyntaxReferences.IsEmpty)
            return false;

        foreach (var parameter in helper.Parameters)
        {
            if (parameter.Type is INamedTypeSymbol { Name: "ModelBuilder" } parameterType &&
                parameterType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore")
            {
                return true;
            }

            if (TryGetEntityTypeBuilderEntity(parameter.Type, out _))
                return true;
        }

        return false;
    }

    private static void ScanAppliedConfiguration(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        CancellationToken cancellationToken)
    {
        var configurationExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var configType = configurationExpression == null
            ? null
            : ResolveConfigurationType(configurationExpression, dbContextType, compilationModel, cancellationToken);
        if (configType == null)
            return;

        if (!TryGetConfiguredEntityType(configType, out var entityType))
            return;

        var (hasKey, hasNoKey) = CheckConfigureMethod(configType, entityType, compilationModel, cancellationToken);

        if (hasKey)
            configuredEntities.Add(entityType);

        if (hasNoKey)
            keylessEntities.Add(entityType);
    }
}
