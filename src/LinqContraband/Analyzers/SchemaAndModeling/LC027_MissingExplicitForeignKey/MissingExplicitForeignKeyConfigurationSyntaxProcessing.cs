using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static void ProcessConfigurationSyntax(
        SyntaxNode syntax,
        CompilationModel compilationModel,
        INamedTypeSymbol? configuredEntityType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
        var relationshipBuilderLocals = BuildRelationshipBuilderLocalMap(
            syntax,
            compilationModel,
            configuredEntityType,
            cancellationToken);

        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName is "OwnsOne" or "OwnsMany")
            {
                var resolvedOwnedType = ResolveOwnedTypeFromConfiguration(
                    invocation,
                    compilationModel,
                    configuredEntityType,
                    cancellationToken);
                if (resolvedOwnedType != null)
                    ownedEntities.Add(resolvedOwnedType);
                continue;
            }

            if (methodName != "HasForeignKey")
                continue;

            var navName = ExtractNavigationNameFromChain(memberAccess.Expression);
            INamedTypeSymbol? localRelationshipEntityType = null;
            if (ExtractRootIdentifierFromReceiverChain(memberAccess.Expression) is IdentifierNameSyntax relationshipLocal &&
                TryResolveRelationshipBuilderLocal(relationshipBuilderLocals, relationshipLocal, out var localRelationship))
            {
                navName = localRelationship.NavigationName;
                localRelationshipEntityType = localRelationship.EntityType;
            }

            if (navName == null)
                continue;

            var entityType = localRelationshipEntityType ?? configuredEntityType;
            if (entityType == null)
            {
                var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
                if (entityTypeName == null)
                    continue;

                entityType = compilationModel.FindTypeByName(entityTypeName, cancellationToken);
            }

            if (entityType != null)
                configuredForeignKeys.Add(GetNavigationConfigurationKey(entityType, navName));
        }
    }

    private static string GetNavigationConfigurationKey(INamedTypeSymbol entityType, string navigationName)
    {
        return entityType.ToDisplayString() + "|" + navigationName;
    }
}
