using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
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
                entityType = entityTypeName != null
                    ? compilationModel.FindTypeByName(entityTypeName, cancellationToken)
                    : ResolveHasOneEntityType(memberAccess.Expression, compilationModel, cancellationToken);
            }

            if (entityType != null)
                configuredForeignKeys.Add(GetNavigationConfigurationKey(entityType, navName));
        }
    }

    /// <summary>
    /// The entity whose <c>HasOne</c> starts the chain, read from the builder's type when the chain does not
    /// start at <c>Entity&lt;T&gt;()</c>: a <c>var b = builder.Entity&lt;T&gt;()</c> local, an
    /// <c>Entity&lt;T&gt;(b =&gt; ...)</c> lambda, or an <c>EntityTypeBuilder&lt;T&gt;</c> parameter.
    /// </summary>
    private static INamedTypeSymbol? ResolveHasOneEntityType(
        ExpressionSyntax expression,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        for (var current = expression; current != null;)
        {
            if (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess })
            {
                if (memberAccess.Name.Identifier.Text == "HasOne")
                {
                    if (!compilationModel.Compilation.TryGetOwnedSemanticModel(memberAccess.SyntaxTree, out var semanticModel))
                        return null;

                    return semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type is INamedTypeSymbol
                    {
                        Name: "EntityTypeBuilder",
                        TypeArguments.Length: 1
                    } builder
                        ? builder.TypeArguments[0] as INamedTypeSymbol
                        : null;
                }

                current = memberAccess.Expression;
                continue;
            }

            current = current is MemberAccessExpressionSyntax nextMemberAccess ? nextMemberAccess.Expression : null;
        }

        return null;
    }

    private static string GetNavigationConfigurationKey(INamedTypeSymbol entityType, string navigationName)
    {
        return entityType.ToDisplayString() + "|" + navigationName;
    }
}
