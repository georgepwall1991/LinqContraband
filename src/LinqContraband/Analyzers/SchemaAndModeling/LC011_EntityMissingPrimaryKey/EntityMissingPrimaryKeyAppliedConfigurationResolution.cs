using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static INamedTypeSymbol? ResolveConfigurationType(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (expression is ObjectCreationExpressionSyntax objectCreation)
            return compilationModel.FindTypeByName(objectCreation.Type.ToString(), cancellationToken);

        if (expression is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
            return ResolveImplicitObjectCreationType(implicitObjectCreation, dbContextType, compilationModel, cancellationToken);

        if (expression is ParenthesizedExpressionSyntax parenthesized)
            return ResolveConfigurationType(parenthesized.Expression, dbContextType, compilationModel, cancellationToken);

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalConfiguration(identifier, dbContextType, compilationModel, cancellationToken, out var localConfigType))
                return localConfigType;

            return TryGetConfigurationTypeFromMember(dbContextType, identifier.Identifier.ValueText, out var memberConfigType)
                ? memberConfigType
                : null;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is ThisExpressionSyntax &&
            TryGetConfigurationTypeFromMember(dbContextType, memberAccess.Name.Identifier.ValueText, out var thisMemberConfigType))
        {
            return thisMemberConfigType;
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveImplicitObjectCreationType(
        ImplicitObjectCreationExpressionSyntax implicitObjectCreation,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var variable = implicitObjectCreation.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        var localDeclaration = variable?.Parent?.Parent as LocalDeclarationStatementSyntax;
        if (localDeclaration != null)
            return compilationModel.FindTypeByName(localDeclaration.Declaration.Type.ToString(), cancellationToken);

        var fieldDeclaration = variable?.Parent?.Parent as FieldDeclarationSyntax;
        if (fieldDeclaration != null)
            return compilationModel.FindTypeByName(fieldDeclaration.Declaration.Type.ToString(), cancellationToken);

        var propertyDeclaration = implicitObjectCreation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propertyDeclaration != null)
            return compilationModel.FindTypeByName(propertyDeclaration.Type.ToString(), cancellationToken);

        return null;
    }

    private static bool TryResolveLocalConfiguration(
        IdentifierNameSyntax identifier,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol configType)
    {
        configType = null!;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                    continue;

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText != identifierName || variable.Initializer?.Value == null)
                        continue;

                    var resolvedConfigType = ResolveConfigurationType(variable.Initializer.Value, dbContextType, compilationModel, cancellationToken);
                    if (resolvedConfigType == null && variable.Initializer.Value is ImplicitObjectCreationExpressionSyntax)
                        resolvedConfigType = compilationModel.FindTypeByName(localDeclaration.Declaration.Type.ToString(), cancellationToken);

                    if (resolvedConfigType != null)
                    {
                        configType = resolvedConfigType;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryGetConfigurationTypeFromMember(
        INamedTypeSymbol dbContextType,
        string memberName,
        out INamedTypeSymbol configType)
    {
        configType = null!;
        foreach (var member in dbContextType.GetMembers(memberName))
        {
            var memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null
            };

            if (memberType is INamedTypeSymbol namedType)
            {
                configType = namedType;
                return true;
            }
        }

        return false;
    }
}
