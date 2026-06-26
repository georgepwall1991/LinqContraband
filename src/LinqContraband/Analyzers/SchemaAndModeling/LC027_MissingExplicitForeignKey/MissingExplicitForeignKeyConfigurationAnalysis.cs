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

    private static bool HasSingleLocalAssignment(
        SyntaxNode scope,
        VariableDeclaratorSyntax declarator,
        CancellationToken cancellationToken)
    {
        var assignmentCount = 1;
        var localName = declarator.Identifier.ValueText;

        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ContainsLocalWriteTarget(assignment.Left, declarator, scope, localName))
                assignmentCount++;
        }

        foreach (var argument in scope.DescendantNodes().OfType<ArgumentSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (argument.RefKindKeyword.ValueText is not ("ref" or "out"))
                continue;

            if (ContainsLocalWriteTarget(argument.Expression, declarator, scope, localName))
                assignmentCount++;
        }

        return assignmentCount == 1;
    }

    private static bool ContainsLocalWriteTarget(
        ExpressionSyntax expression,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => IsLocalWriteTarget(identifier, declarator, scope, localName),
            ParenthesizedExpressionSyntax parenthesized => ContainsLocalWriteTarget(parenthesized.Expression, declarator, scope, localName),
            TupleExpressionSyntax tuple => tuple.Arguments.Any(argument =>
                ContainsLocalWriteTarget(argument.Expression, declarator, scope, localName)),
            _ => false
        };
    }

    private static bool IsLocalWriteTarget(
        IdentifierNameSyntax identifier,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        return identifier.Identifier.ValueText == localName &&
               IsVisibleAt(declarator, scope, identifier) &&
               !IsShadowedByNestedLocal(identifier, declarator, scope, localName);
    }

    private static bool IsShadowedByNestedLocal(
        IdentifierNameSyntax reference,
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        string localName)
    {
        foreach (var currentDeclarator in scope.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (currentDeclarator == declarator ||
                currentDeclarator.Identifier.ValueText != localName ||
                FindLocalScope(currentDeclarator) is not SyntaxNode currentScope ||
                currentScope == scope)
            {
                continue;
            }

            if (currentDeclarator.SpanStart > declarator.SpanStart &&
                IsVisibleAt(currentDeclarator, currentScope, reference))
            {
                return true;
            }
        }

        foreach (var designation in scope.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
        {
            if (designation.Identifier.ValueText != localName ||
                FindDesignationScope(designation) is not SyntaxNode designationScope ||
                designationScope == scope)
            {
                continue;
            }

            if (designation.SpanStart > declarator.SpanStart &&
                designationScope.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        foreach (var foreachStatement in scope.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (foreachStatement.Identifier.ValueText != localName ||
                FindStatementScope(foreachStatement) is not SyntaxNode foreachScope ||
                foreachScope == scope)
            {
                continue;
            }

            if (foreachStatement.SpanStart > declarator.SpanStart &&
                foreachStatement.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        foreach (var parameter in scope.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Identifier.ValueText != localName ||
                FindParameterScope(parameter) is not SyntaxNode parameterScope ||
                parameterScope == scope)
            {
                continue;
            }

            if (parameter.SpanStart > declarator.SpanStart &&
                parameterScope.Span.Contains(reference.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? FindDesignationScope(SingleVariableDesignationSyntax designation)
    {
        return designation.Ancestors().FirstOrDefault(node =>
            node is BlockSyntax or
                SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindStatementScope(StatementSyntax statement)
    {
        return statement.Ancestors().FirstOrDefault(node =>
            node is BlockSyntax or
                SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindParameterScope(ParameterSyntax parameter)
    {
        return parameter.Ancestors().FirstOrDefault(node =>
            node is SimpleLambdaExpressionSyntax or
                ParenthesizedLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax or
                LocalFunctionStatementSyntax or
                MethodDeclarationSyntax or
                ConstructorDeclarationSyntax);
    }

    private static SyntaxNode? FindLocalScope(VariableDeclaratorSyntax declarator)
    {
        return declarator.Parent?.Parent?.Parent is BlockSyntax block
            ? block
            : declarator.Ancestors().FirstOrDefault(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax);
    }

    private static bool IsVisibleAt(
        VariableDeclaratorSyntax declarator,
        SyntaxNode scope,
        SyntaxNode reference)
    {
        return reference.SpanStart >= declarator.SpanStart &&
               scope.Span.Contains(reference.SpanStart);
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
