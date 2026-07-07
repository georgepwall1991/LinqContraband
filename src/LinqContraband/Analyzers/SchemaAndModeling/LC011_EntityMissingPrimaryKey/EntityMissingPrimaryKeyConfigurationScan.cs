using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            var builderVariables = new Dictionary<string, INamedTypeSymbol>();

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "HasKey" &&
                    TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var configuredEntity))
                {
                    configuredEntities.Add(configuredEntity);
                }
                else if (methodName == "HasNoKey" &&
                         TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var keylessEntity))
                {
                    keylessEntities.Add(keylessEntity);
                }
                else if (methodName is "OwnsOne" or "OwnsMany" &&
                         TryGetOwnedEntityType(invocation, memberAccess, builderVariables, compilationModel, cancellationToken, out var ownedEntity))
                {
                    ownedEntities.Add(ownedEntity);
                }
                else if (methodName == "ApplyConfiguration")
                {
                    ScanAppliedConfiguration(invocation, dbContextType, compilationModel, configuredEntities, keylessEntities, cancellationToken);
                }
                else if (methodName == "ApplyConfigurationsFromAssembly" &&
                         ShouldScanCurrentAssemblyConfigurations(invocation, dbContextType, compilationModel, cancellationToken))
                {
                    ScanEntityTypeConfigurations(compilationModel, configuredEntities, keylessEntities, cancellationToken);
                }
            }
        }
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
