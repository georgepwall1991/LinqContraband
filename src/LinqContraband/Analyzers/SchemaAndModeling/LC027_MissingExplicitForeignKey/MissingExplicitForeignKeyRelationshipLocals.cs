using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static List<RelationshipConfiguration> BuildRelationshipBuilderLocalMap(
        SyntaxNode syntax,
        CompilationModel compilationModel,
        INamedTypeSymbol? configuredEntityType,
        CancellationToken cancellationToken)
    {
        var result = new List<RelationshipConfiguration>();

        foreach (var declarator in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (declarator.Initializer?.Value is not InvocationExpressionSyntax initializer ||
                FindLocalScope(declarator) is not SyntaxNode scope ||
                !HasSingleLocalAssignment(scope, declarator, cancellationToken))
            {
                continue;
            }

            var navName = ExtractNavigationNameFromChain(initializer);
            if (navName == null)
                continue;

            var entityType = configuredEntityType;
            if (entityType == null)
            {
                var entityTypeName = ExtractEntityTypeNameFromChain(initializer);
                if (entityTypeName == null)
                    continue;

                entityType = compilationModel.FindTypeByName(entityTypeName, cancellationToken);
            }

            if (entityType != null)
                result.Add(new RelationshipConfiguration(declarator.Identifier.ValueText, declarator, scope, entityType, navName));
        }

        return result;
    }

    private static bool TryResolveRelationshipBuilderLocal(
        List<RelationshipConfiguration> relationshipBuilderLocals,
        IdentifierNameSyntax reference,
        out RelationshipConfiguration relationship)
    {
        relationship = null!;

        foreach (var candidate in relationshipBuilderLocals)
        {
            if (candidate.Name != reference.Identifier.ValueText ||
                !IsVisibleAt(candidate.Declarator, candidate.Scope, reference) ||
                IsShadowedByNestedLocal(reference, candidate.Declarator, candidate.Scope, candidate.Name))
            {
                continue;
            }

            if (relationship == null ||
                candidate.Declarator.SpanStart > relationship.Declarator.SpanStart)
            {
                relationship = candidate;
            }
        }

        return relationship != null;
    }

    private static IdentifierNameSyntax? ExtractRootIdentifierFromReceiverChain(ExpressionSyntax expression)
    {
        var current = expression;
        while (current != null)
        {
            switch (current)
            {
                case IdentifierNameSyntax identifier:
                    return identifier;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }:
                    current = memberAccess.Expression;
                    continue;
                case MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    private sealed class RelationshipConfiguration
    {
        public RelationshipConfiguration(
            string name,
            VariableDeclaratorSyntax declarator,
            SyntaxNode scope,
            INamedTypeSymbol entityType,
            string navigationName)
        {
            Name = name;
            Declarator = declarator;
            Scope = scope;
            EntityType = entityType;
            NavigationName = navigationName;
        }

        public string Name { get; }

        public VariableDeclaratorSyntax Declarator { get; }

        public SyntaxNode Scope { get; }

        public INamedTypeSymbol EntityType { get; }

        public string NavigationName { get; }
    }
}
