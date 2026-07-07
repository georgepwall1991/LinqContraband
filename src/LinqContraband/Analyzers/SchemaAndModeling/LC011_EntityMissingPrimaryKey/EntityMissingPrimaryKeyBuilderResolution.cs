using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static Dictionary<string, INamedTypeSymbol> CollectConfigureBuilderParameters(IMethodSymbol configureMethod)
    {
        var builderVariables = new Dictionary<string, INamedTypeSymbol>();
        foreach (var parameter in configureMethod.Parameters)
        {
            if (TryGetEntityTypeBuilderEntity(parameter.Type, out var entityType))
                builderVariables[parameter.Name] = entityType;
        }

        return builderVariables;
    }

    private static bool TryResolveEntityTypeFromBuilderExpression(
        ExpressionSyntax expression,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol entityType)
    {
        return TryResolveEntityTypeFromBuilderExpression(
            expression,
            builderVariables,
            compilationModel,
            cancellationToken,
            new HashSet<ExpressionSyntax>(),
            out entityType);
    }

    private static bool TryResolveEntityTypeFromBuilderExpression(
        ExpressionSyntax expression,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions,
        out INamedTypeSymbol entityType)
    {
        entityType = null!;
        cancellationToken.ThrowIfCancellationRequested();

        if (!visitedExpressions.Add(expression))
            return false;

        if (ExtractEntityTypeNameFromChain(expression) is { } entityTypeName)
        {
            var resolvedEntityType = compilationModel.FindTypeByName(entityTypeName, cancellationToken);
            if (resolvedEntityType != null)
            {
                entityType = resolvedEntityType;
                return true;
            }
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess &&
            TryResolveEntityTypeFromBuilderExpression(chainedMemberAccess.Expression, builderVariables, compilationModel, cancellationToken, visitedExpressions, out var chainedEntityType))
        {
            entityType = chainedEntityType;
            return true;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized &&
            TryResolveEntityTypeFromBuilderExpression(parenthesized.Expression, builderVariables, compilationModel, cancellationToken, visitedExpressions, out var parenthesizedEntityType))
        {
            entityType = parenthesizedEntityType;
            return true;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalBuilder(identifier, builderVariables, compilationModel, cancellationToken, visitedExpressions, out var localEntityType))
            {
                entityType = localEntityType;
                return true;
            }

            if (builderVariables.TryGetValue(identifier.Identifier.ValueText, out var parameterEntityType))
            {
                entityType = parameterEntityType;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEntityTypeBuilderEntity(ITypeSymbol? type, out INamedTypeSymbol entityType)
    {
        entityType = null!;

        if (type is not INamedTypeSymbol namedType ||
            namedType.Name != "EntityTypeBuilder" ||
            namedType.TypeArguments.Length == 0)
        {
            return false;
        }

        var namespaceName = namedType.ContainingNamespace?.ToString();
        if (namespaceName is not ("Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Metadata.Builders"))
            return false;

        if (namedType.TypeArguments[0] is not INamedTypeSymbol builderEntity)
            return false;

        entityType = builderEntity;
        return true;
    }

}
