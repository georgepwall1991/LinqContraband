using System.Collections.Generic;
using System.Linq;
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
        Compilation compilation)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var builderVariables = new Dictionary<string, INamedTypeSymbol>();

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "HasKey" &&
                    TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilation, out var configuredEntity))
                {
                    configuredEntities.Add(configuredEntity);
                }
                else if (methodName == "HasNoKey" &&
                         TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilation, out var keylessEntity))
                {
                    keylessEntities.Add(keylessEntity);
                }
                else if (methodName is "OwnsOne" or "OwnsMany" &&
                         TryGetOwnedEntityType(invocation, memberAccess, builderVariables, compilation, out var ownedEntity))
                {
                    ownedEntities.Add(ownedEntity);
                }
                else if (methodName == "ApplyConfiguration")
                {
                    ScanAppliedConfiguration(invocation, dbContextType, compilation, configuredEntities, keylessEntities);
                }
                else if (methodName == "ApplyConfigurationsFromAssembly" &&
                         ShouldScanCurrentAssemblyConfigurations(invocation, compilation))
                {
                    ScanEntityTypeConfigurations(compilation, configuredEntities, keylessEntities);
                }
            }
        }
    }

    private static void ScanEntityTypeConfigurations(
        Compilation compilation,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities)
    {
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return;

        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.AllInterfaces.IsEmpty)
                continue;

            foreach (var iface in type.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface))
                    continue;

                if (iface.TypeArguments.Length > 0 &&
                    iface.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    var (hasKey, hasNoKey) = CheckConfigureMethod(type, entityType, compilation);

                    if (hasKey)
                        configuredEntities.Add(entityType);

                    if (hasNoKey)
                        keylessEntities.Add(entityType);
                }
            }
        }
    }

    private static void ScanAppliedConfiguration(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol dbContextType,
        Compilation compilation,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities)
    {
        var configurationExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var configType = configurationExpression == null
            ? null
            : ResolveConfigurationType(configurationExpression, dbContextType, compilation);
        if (configType == null)
            return;

        if (!TryGetConfiguredEntityType(configType, out var entityType))
            return;

        var (hasKey, hasNoKey) = CheckConfigureMethod(configType, entityType, compilation);

        if (hasKey)
            configuredEntities.Add(entityType);

        if (hasNoKey)
            keylessEntities.Add(entityType);
    }

    private static bool ShouldScanCurrentAssemblyConfigurations(InvocationExpressionSyntax invocation, Compilation compilation)
    {
        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Assembly" ||
            memberAccess.Expression is not TypeOfExpressionSyntax typeOfExpression)
        {
            return false;
        }

        var assemblyMarkerType = FindTypeByName(compilation, typeOfExpression.Type.ToString());
        return assemblyMarkerType != null &&
               SymbolEqualityComparer.Default.Equals(assemblyMarkerType.ContainingAssembly, compilation.Assembly);
    }

    private static INamedTypeSymbol? ResolveConfigurationType(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        Compilation compilation)
    {
        if (expression is ObjectCreationExpressionSyntax objectCreation)
            return FindTypeByName(compilation, objectCreation.Type.ToString());

        if (expression is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
            return ResolveImplicitObjectCreationType(implicitObjectCreation, dbContextType, compilation);

        if (expression is ParenthesizedExpressionSyntax parenthesized)
            return ResolveConfigurationType(parenthesized.Expression, dbContextType, compilation);

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalConfiguration(identifier, dbContextType, compilation, out var localConfigType))
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
        Compilation compilation)
    {
        var variable = implicitObjectCreation.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        var localDeclaration = variable?.Parent?.Parent as LocalDeclarationStatementSyntax;
        if (localDeclaration != null)
            return FindTypeByName(compilation, localDeclaration.Declaration.Type.ToString());

        var fieldDeclaration = variable?.Parent?.Parent as FieldDeclarationSyntax;
        if (fieldDeclaration != null)
            return FindTypeByName(compilation, fieldDeclaration.Declaration.Type.ToString());

        var propertyDeclaration = implicitObjectCreation.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propertyDeclaration != null)
            return FindTypeByName(compilation, propertyDeclaration.Type.ToString());

        return null;
    }

    private static bool TryResolveLocalConfiguration(
        IdentifierNameSyntax identifier,
        INamedTypeSymbol dbContextType,
        Compilation compilation,
        out INamedTypeSymbol configType)
    {
        configType = null!;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                    continue;

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText != identifierName || variable.Initializer?.Value == null)
                        continue;

                    var resolvedConfigType = ResolveConfigurationType(variable.Initializer.Value, dbContextType, compilation);
                    if (resolvedConfigType == null && variable.Initializer.Value is ImplicitObjectCreationExpressionSyntax)
                        resolvedConfigType = FindTypeByName(compilation, localDeclaration.Declaration.Type.ToString());

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

    private static (bool hasKey, bool hasNoKey) CheckConfigureMethod(
        INamedTypeSymbol configClass,
        INamedTypeSymbol entityType,
        Compilation compilation)
    {
        var configureMethod = configClass.GetMembers("Configure").FirstOrDefault() as IMethodSymbol;
        if (configureMethod == null)
            return (false, false);

        var hasKey = false;
        var hasNoKey = false;

        foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            var builderVariables = CollectConfigureBuilderParameters(configureMethod);
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilation, out var configuredEntity) ||
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
        Compilation compilation,
        out INamedTypeSymbol entityType)
    {
        entityType = null!;

        if (ExtractEntityTypeNameFromChain(expression) is { } entityTypeName)
        {
            var resolvedEntityType = FindTypeByName(compilation, entityTypeName);
            if (resolvedEntityType != null)
            {
                entityType = resolvedEntityType;
                return true;
            }
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess &&
            TryResolveEntityTypeFromBuilderExpression(chainedMemberAccess.Expression, builderVariables, compilation, out var chainedEntityType))
        {
            entityType = chainedEntityType;
            return true;
        }

        if (expression is ParenthesizedExpressionSyntax parenthesized &&
            TryResolveEntityTypeFromBuilderExpression(parenthesized.Expression, builderVariables, compilation, out var parenthesizedEntityType))
        {
            entityType = parenthesizedEntityType;
            return true;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalBuilder(identifier, builderVariables, compilation, out var localEntityType))
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

    private static bool TryResolveLocalBuilder(
        IdentifierNameSyntax identifier,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        Compilation compilation,
        out INamedTypeSymbol entityType)
    {
        entityType = null!;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                    continue;

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText != identifierName || variable.Initializer?.Value == null)
                        continue;

                    if (TryResolveEntityTypeFromBuilderExpression(variable.Initializer.Value, builderVariables, compilation, out var localEntityType))
                    {
                        entityType = localEntityType;
                        return true;
                    }
                }
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

    private static bool TryGetOwnedEntityType(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        Compilation compilation,
        out INamedTypeSymbol ownedEntity)
    {
        ownedEntity = null!;

        if (memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
            {
                var resolvedOwnedType = FindTypeByName(compilation, typeArg.ToString());
                if (resolvedOwnedType != null)
                {
                    ownedEntity = resolvedOwnedType;
                    return true;
                }
            }
        }

        if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilation, out var ownerEntity))
            return false;

        var lambda = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression as LambdaExpressionSyntax;
        if (lambda?.Body is not MemberAccessExpressionSyntax navigationAccess)
            return false;

        var navigationName = navigationAccess.Name.Identifier.ValueText;
        var navigation = ownerEntity.GetMembers(navigationName).OfType<IPropertySymbol>().FirstOrDefault();
        if (navigation?.Type is not INamedTypeSymbol navigationType)
            return false;

        ownedEntity = TryGetCollectionElementType(navigationType) ?? navigationType;
        return true;
    }

    private static INamedTypeSymbol? TryGetCollectionElementType(INamedTypeSymbol navigationType)
    {
        if (navigationType.SpecialType == SpecialType.System_String)
            return null;

        foreach (var iface in navigationType.AllInterfaces)
        {
            if (iface.Name == "IEnumerable" &&
                iface.ContainingNamespace?.ToString() == "System.Collections.Generic" &&
                iface.TypeArguments.Length == 1 &&
                iface.TypeArguments[0] is INamedTypeSymbol elementType)
            {
                return elementType;
            }
        }

        return null;
    }
}
