using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;

public sealed partial class MissingExplicitForeignKeyAnalyzer
{
    private static void ScanOnModelCreating(
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
        var methods = dbContextType.GetMembers("OnModelCreating");
        if (methods.IsEmpty || methods[0] is not IMethodSymbol onModelCreating)
            return;

        foreach (var syntaxRef in onModelCreating.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            ProcessConfigurationSyntax(syntax, compilationModel, null, ownedEntities, configuredForeignKeys, cancellationToken);
        }
    }

    private static void ScanEntityTypeConfigurations(
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
        var scan = compilationModel.GetConfigurationScan(cancellationToken);
        ownedEntities.UnionWith(scan.OwnedEntities);
        configuredForeignKeys.UnionWith(scan.ConfiguredForeignKeys);
    }

    private static ConfigurationScan BuildConfigurationScan(
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var scan = new ConfigurationScan();
        var compilation = compilationModel.Compilation;
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return scan;

        foreach (var type in compilationModel.GetAllTypes(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var iface in type.AllInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    cancellationToken.ThrowIfCancellationRequested();
                    var syntax = syntaxRef.GetSyntax(cancellationToken);
                    ProcessConfigurationSyntax(
                        syntax,
                        compilationModel,
                        entityType,
                        scan.OwnedEntities,
                        scan.ConfiguredForeignKeys,
                        cancellationToken);
                }
            }
        }

        return scan;
    }

    private static void ProcessConfigurationSyntax(
        SyntaxNode syntax,
        CompilationModel compilationModel,
        INamedTypeSymbol? configuredEntityType,
        HashSet<INamedTypeSymbol> ownedEntities,
        HashSet<string> configuredForeignKeys,
        CancellationToken cancellationToken)
    {
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
            if (navName == null)
                continue;

            var entityType = configuredEntityType;
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

    private static string? ExtractNavigationNameFromChain(ExpressionSyntax expression)
    {
        var current = expression;
        string? withOneNavigationName = null;

        while (current != null)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var methodName = memberAccess.Name.Identifier.Text;
                if (methodName == "HasOne")
                {
                    var navName = ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
                    if (navName != null)
                        return navName;
                }
                else if (methodName == "WithOne")
                {
                    withOneNavigationName ??= ExtractNavigationNameFromArgument(invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);
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

        return withOneNavigationName;
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
        CompilationModel compilationModel,
        INamedTypeSymbol? configuredEntityType,
        CancellationToken cancellationToken)
    {
        var explicitTypeName = ExtractOwnedTypeName(invocation);
        if (explicitTypeName != null)
            return compilationModel.FindTypeByName(explicitTypeName, cancellationToken);

        var entityType = configuredEntityType;
        if (entityType == null &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var entityTypeName = ExtractEntityTypeNameFromChain(memberAccess.Expression);
            if (entityTypeName != null)
                entityType = compilationModel.FindTypeByName(entityTypeName, cancellationToken);
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
