using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        Compilation compilation,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            ProcessConfigurationSyntax(syntax, compilation, null, ownedEntities, configuredForeignKeys);
        }
    }

    private static void ScanEntityTypeConfigurations(
        Compilation compilation,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return;

        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
        {
            foreach (var iface in type.AllInterfaces)
            {
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
                    var syntax = syntaxRef.GetSyntax();
                    ProcessConfigurationSyntax(syntax, compilation, entityType, ownedEntities, configuredForeignKeys);
                }
            }
        }
    }

    private static void ProcessConfigurationSyntax(
        SyntaxNode syntax,
        Compilation compilation,
        INamedTypeSymbol? configuredEntityType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys)
    {
        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;
            if (methodName is "OwnsOne" or "OwnsMany")
            {
                var resolvedOwnedType = ResolveOwnedTypeFromConfiguration(invocation, compilation, configuredEntityType);
                if (resolvedOwnedType != null)
                    ownedEntities.Add(resolvedOwnedType);
                continue;
            }

            if (methodName != "HasForeignKey")
                continue;

            var navName = ExtractNavigationNameFromChain(memberAccess.Expression);
            if (navName == null)
                continue;

            var entityType = configuredEntityType;
            if (entityType == null)
            {
                var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
                if (entityTypeName == null)
                    continue;

                entityType = FindTypeByName(compilation, entityTypeName);
            }

            if (entityType != null)
                configuredForeignKeys.Add(GetNavigationConfigurationKey(entityType, navName));
        }
    }

    private static string GetNavigationConfigurationKey(INamedTypeSymbol entityType, string navigationName)
    {
        return entityType.ToDisplayString() + "|" + navigationName;
    }

    private static string? ExtractNavigationNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;
        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName is "HasOne" or "HasMany" or "WithOne" or "WithMany")
                {
                    var navName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                    if (navName != null)
                        return navName;
                }

                current = memberAccess.Expression;
                continue;
            }

            current = current switch
            {
                MemberAccessExpressionSyntax nextMemberAccess => nextMemberAccess.Expression,
                _ => null
            };
        }

        return null;
    }

    private static string? ExtractNavigationNameFromArgument(ExpressionSyntax? argument)
    {
        return argument switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => ExtractMemberName(simpleLambda.Body),
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => ExtractMemberName(parenthesizedLambda.Body),
            _ => null
        };
    }

    private static string? ExtractMemberName(SyntaxNode body)
    {
        return body switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private static string? ExtractEntityTypeNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.Text == "Entity")
            {
                var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
                if (typeArg != null)
                    return typeArg.ToString();
            }

            current = current switch
            {
                InvocationExpressionSyntax inv => inv.Expression,
                MemberAccessExpressionSyntax ma => ma.Expression,
                _ => null
            };
        }

        return null;
    }

    private static string? ExtractOwnedTypeName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
                return typeArg.ToString();
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveOwnedTypeFromConfiguration(
        InvocationExpressionSyntax invocation,
        Compilation compilation,
        INamedTypeSymbol? configuredEntityType)
    {
        var explicitTypeName = ExtractOwnedTypeName(invocation);
        if (explicitTypeName != null)
            return FindTypeByName(compilation, explicitTypeName);

        var entityType = configuredEntityType;
        if (entityType == null &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
            if (entityTypeName != null)
                entityType = FindTypeByName(compilation, entityTypeName);
        }

        if (entityType == null)
            return null;

        var ownedNavigationName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
        if (ownedNavigationName == null)
            return null;

        return entityType.GetMembers(ownedNavigationName)
            .OfType<IPropertySymbol>()
            .Select(property => property.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault();
    }
}
