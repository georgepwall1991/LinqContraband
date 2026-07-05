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

    private static void ScanEntityTypeConfigurations(
        CompilationModel compilationModel,
        HashSet<INamedTypeSymbol> configuredEntities,
        HashSet<INamedTypeSymbol> keylessEntities,
        CancellationToken cancellationToken)
    {
        var scan = compilationModel.GetEntityTypeConfigurationScan(cancellationToken);
        configuredEntities.UnionWith(scan.ConfiguredEntities);
        keylessEntities.UnionWith(scan.KeylessEntities);
    }

    private static EntityTypeConfigurationScan BuildEntityTypeConfigurationScan(
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var scan = new EntityTypeConfigurationScan();
        var compilation = compilationModel.Compilation;
        var configInterface = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return scan;

        foreach (var type in compilationModel.GetAllTypes(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (type.AllInterfaces.IsEmpty)
                continue;

            foreach (var iface in type.AllInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface))
                    continue;

                if (iface.TypeArguments.Length > 0 &&
                    iface.TypeArguments[0] is INamedTypeSymbol entityType)
                {
                    var (hasKey, hasNoKey) = CheckConfigureMethod(type, entityType, compilationModel, cancellationToken);

                    if (hasKey)
                        scan.ConfiguredEntities.Add(entityType);

                    if (hasNoKey)
                        scan.KeylessEntities.Add(entityType);
                }
            }
        }

        return scan;
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

    private static bool ShouldScanCurrentAssemblyConfigurations(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var assemblyExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        return assemblyExpression != null &&
               IsCurrentAssemblyExpression(
                   assemblyExpression,
                   dbContextType,
                   compilationModel,
                   cancellationToken,
                   new HashSet<ExpressionSyntax>());
    }

    private static bool IsCurrentAssemblyExpression(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visitedExpressions.Add(expression))
            return false;

        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return IsCurrentAssemblyExpression(
                parenthesized.Expression,
                dbContextType,
                compilationModel,
                cancellationToken,
                visitedExpressions);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "Assembly" &&
            memberAccess.Expression is TypeOfExpressionSyntax typeOfExpression)
        {
            return IsCurrentAssemblyMarker(typeOfExpression, compilationModel, cancellationToken);
        }

        if (IsGetExecutingAssemblyCall(expression, dbContextType, compilationModel, cancellationToken))
            return true;

        if (expression is IdentifierNameSyntax identifier)
        {
            if (TryResolveLocalCurrentAssembly(identifier, dbContextType, compilationModel, cancellationToken, visitedExpressions, out var localIsCurrentAssembly))
                return localIsCurrentAssembly;

            return TryResolveMemberCurrentAssembly(dbContextType, identifier.Identifier.ValueText, compilationModel, cancellationToken, visitedExpressions);
        }

        if (expression is MemberAccessExpressionSyntax thisMemberAccess &&
            thisMemberAccess.Expression is ThisExpressionSyntax)
        {
            return TryResolveMemberCurrentAssembly(
                dbContextType,
                thisMemberAccess.Name.Identifier.ValueText,
                compilationModel,
                cancellationToken,
                visitedExpressions);
        }

        return false;
    }

    private static bool IsCurrentAssemblyMarker(
        TypeOfExpressionSyntax typeOfExpression,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var assemblyMarkerType = compilationModel.FindTypeByName(typeOfExpression.Type.ToString(), cancellationToken);
        return assemblyMarkerType != null &&
               SymbolEqualityComparer.Default.Equals(assemblyMarkerType.ContainingAssembly, compilationModel.Compilation.Assembly);
    }

    private static bool IsGetExecutingAssemblyCall(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.ArgumentList.Arguments.Count != 0)
        {
            return false;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.ValueText == "GetExecutingAssembly" &&
               IsAssemblyTypeExpression(memberAccess.Expression, dbContextType, compilationModel, cancellationToken);
    }

    private static bool IsAssemblyTypeExpression(
        ExpressionSyntax expression,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (expression.ToString() is "System.Reflection.Assembly" or "global::System.Reflection.Assembly")
            return true;

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Assembly" &&
                                               !HasLocalAssemblyValueInScope(identifier, cancellationToken) &&
                                               !HasAssemblyMember(dbContextType) &&
                                               (HasSystemReflectionAssemblyAliasInScope(identifier, compilationModel, cancellationToken) ||
                                                HasSystemReflectionUsing(identifier, compilationModel, cancellationToken)) &&
                                               !HasAssemblyAliasInScope(identifier, compilationModel, cancellationToken) &&
                                               !HasVisibleAssemblyType(dbContextType, compilationModel.Compilation.GetTypeByMetadataName("System.Reflection.Assembly")),
            _ => false
        };
    }

    private static bool HasLocalAssemblyValueInScope(
        IdentifierNameSyntax identifier,
        CancellationToken cancellationToken)
    {
        var position = identifier.SpanStart;

        foreach (var parameterList in identifier.Ancestors().OfType<ParameterListSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parameterList.SpanStart >= position)
                continue;

            if (parameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "Assembly"))
                return true;
        }

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (DeclaresAssemblyValue(statement))
                    return true;
            }
        }

        return false;
    }

    private static bool DeclaresAssemblyValue(StatementSyntax statement)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration &&
            localDeclaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Assembly"))
        {
            return true;
        }

        if (statement is ForEachStatementSyntax foreachStatement &&
            foreachStatement.Identifier.ValueText == "Assembly")
        {
            return true;
        }

        if (statement.DescendantNodes().OfType<ForEachStatementSyntax>().Any(foreachStatement => foreachStatement.Identifier.ValueText == "Assembly"))
            return true;

        if (statement.DescendantNodes().OfType<CatchDeclarationSyntax>().Any(catchDeclaration => catchDeclaration.Identifier.ValueText == "Assembly"))
            return true;

        return statement.DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Any(designation => designation.Identifier.ValueText == "Assembly");
    }

    private static bool HasAssemblyMember(INamedTypeSymbol dbContextType)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.BaseType)
        {
            if (currentType.GetMembers("Assembly").Any(member => member is IFieldSymbol or IPropertySymbol or IMethodSymbol))
                return true;
        }

        return false;
    }

    private static bool HasAssemblyAliasInScope(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax currentCompilationUnit &&
            currentCompilationUnit.Usings.Any(IsNonSystemReflectionAssemblyAlias))
        {
            return true;
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (namespaceDeclaration.Usings.Any(IsNonSystemReflectionAssemblyAlias))
                return true;
        }

        foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileScopedNamespace.Usings.Any(IsNonSystemReflectionAssemblyAlias))
                return true;
        }

        foreach (var syntaxTree in compilationModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax compilationUnit)
                continue;

            if (compilationUnit.Usings.Any(usingDirective =>
                    usingDirective.GlobalKeyword.RawKind != 0 &&
                    IsNonSystemReflectionAssemblyAlias(usingDirective)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSystemReflectionAssemblyAliasInScope(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax currentCompilationUnit &&
            currentCompilationUnit.Usings.Any(IsSystemReflectionAssemblyAlias))
        {
            return true;
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (namespaceDeclaration.Usings.Any(IsSystemReflectionAssemblyAlias))
                return true;
        }

        foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileScopedNamespace.Usings.Any(IsSystemReflectionAssemblyAlias))
                return true;
        }

        foreach (var syntaxTree in compilationModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax compilationUnit)
                continue;

            if (compilationUnit.Usings.Any(usingDirective =>
                    usingDirective.GlobalKeyword.RawKind != 0 &&
                    IsSystemReflectionAssemblyAlias(usingDirective)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVisibleAssemblyType(
        INamedTypeSymbol dbContextType,
        INamedTypeSymbol? systemReflectionAssemblyType)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.ContainingType)
        {
            if (currentType.GetTypeMembers("Assembly").Any(type => !IsSystemReflectionAssemblyType(type, systemReflectionAssemblyType)))
                return true;
        }

        for (var currentNamespace = dbContextType.ContainingNamespace;
             currentNamespace != null;
             currentNamespace = currentNamespace.ContainingNamespace)
        {
            if (currentNamespace.GetTypeMembers("Assembly").Any(type => !IsSystemReflectionAssemblyType(type, systemReflectionAssemblyType)))
                return true;

            if (currentNamespace.IsGlobalNamespace)
                break;
        }

        return false;
    }

    private static bool HasSystemReflectionUsing(
        SyntaxNode node,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax currentCompilationUnit &&
            currentCompilationUnit.Usings.Any(IsSystemReflectionUsing))
        {
            return true;
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<NamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (namespaceDeclaration.Usings.Any(IsSystemReflectionUsing))
                return true;
        }

        foreach (var fileScopedNamespace in node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fileScopedNamespace.Usings.Any(IsSystemReflectionUsing))
                return true;
        }

        foreach (var syntaxTree in compilationModel.Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxTree == node.SyntaxTree ||
                syntaxTree.GetRoot(cancellationToken) is not CompilationUnitSyntax compilationUnit)
            {
                continue;
            }

            if (compilationUnit.Usings.Any(IsGlobalSystemReflectionUsing))
                return true;
        }

        return false;
    }

    private static bool IsSystemReflectionUsing(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Name?.ToString() == "System.Reflection";
    }

    private static bool IsNonSystemReflectionAssemblyAlias(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias?.Name.Identifier.ValueText == "Assembly" &&
               !IsSystemReflectionAssemblyAlias(usingDirective);
    }

    private static bool IsSystemReflectionAssemblyAlias(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias?.Name.Identifier.ValueText == "Assembly" &&
               usingDirective.Name?.ToString() is "System.Reflection.Assembly" or "global::System.Reflection.Assembly";
    }

    private static bool IsSystemReflectionAssemblyType(
        INamedTypeSymbol type,
        INamedTypeSymbol? systemReflectionAssemblyType)
    {
        return systemReflectionAssemblyType != null &&
               SymbolEqualityComparer.Default.Equals(type, systemReflectionAssemblyType);
    }

    private static bool IsGlobalSystemReflectionUsing(UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.GlobalKeyword.RawKind != 0 &&
               IsSystemReflectionUsing(usingDirective);
    }

    private static bool TryResolveLocalCurrentAssembly(
        IdentifierNameSyntax identifier,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions,
        out bool isCurrentAssembly)
    {
        isCurrentAssembly = false;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpressionSyntax? latestValue = null;
            var localFound = false;

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                {
                    if (TryGetLocalAssignment(statement, identifierName, out var assignedValue))
                    {
                        localFound = true;
                        latestValue = assignedValue;
                    }
                    else if (ContainsLocalAssignment(statement, identifierName))
                    {
                        return true;
                    }

                    continue;
                }

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText == identifierName)
                    {
                        localFound = true;
                        latestValue = variable.Initializer?.Value;
                    }
                }
            }

            if (latestValue != null)
            {
                isCurrentAssembly = IsCurrentAssemblyExpression(latestValue, dbContextType, compilationModel, cancellationToken, visitedExpressions);
                return true;
            }

            if (localFound)
                return true;
        }

        return false;
    }

    private static bool TryGetLocalAssignment(
        StatementSyntax statement,
        string localName,
        out ExpressionSyntax assignedValue)
    {
        assignedValue = null!;
        if (statement is not ExpressionStatementSyntax expressionStatement ||
            expressionStatement.Expression is not AssignmentExpressionSyntax assignment ||
            assignment.Left is not IdentifierNameSyntax identifier ||
            identifier.Identifier.ValueText != localName)
        {
            return false;
        }

        assignedValue = assignment.Right;
        return true;
    }

    private static bool ContainsLocalAssignment(SyntaxNode node, string localName)
    {
        if (node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            return false;

        return node.DescendantNodes(descendIntoChildren: child =>
                child is not AnonymousFunctionExpressionSyntax &&
                child is not LocalFunctionStatementSyntax)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is IdentifierNameSyntax identifier &&
                               identifier.Identifier.ValueText == localName);
    }

    private static bool TryResolveMemberCurrentAssembly(
        INamedTypeSymbol dbContextType,
        string memberName,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.BaseType)
        {
            var members = currentType.GetMembers(memberName);
            if (members.IsEmpty)
                continue;

            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member.DeclaringSyntaxReferences.IsEmpty)
                    continue;

                foreach (var syntaxRef in member.DeclaringSyntaxReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var syntax = syntaxRef.GetSyntax(cancellationToken);
                    ExpressionSyntax? initializer = syntax switch
                    {
                        VariableDeclaratorSyntax variable when member is IFieldSymbol { IsReadOnly: true } => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property when member is IPropertySymbol { SetMethod: null } => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                        _ => null
                    };

                    if (initializer != null &&
                        IsCurrentAssemblyExpression(initializer, dbContextType, compilationModel, cancellationToken, visitedExpressions))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return false;
    }

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

    private static (bool hasKey, bool hasNoKey) CheckConfigureMethod(
        INamedTypeSymbol configClass,
        INamedTypeSymbol entityType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken)
    {
        var configureMethod = configClass.GetMembers("Configure").FirstOrDefault() as IMethodSymbol;
        if (configureMethod == null)
            return (false, false);

        var hasKey = false;
        var hasNoKey = false;

        foreach (var syntaxRef in configureMethod.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxRef.GetSyntax(cancellationToken);
            var builderVariables = CollectConfigureBuilderParameters(configureMethod);
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var configuredEntity) ||
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

    private static bool TryResolveLocalBuilder(
        IdentifierNameSyntax identifier,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions,
        out INamedTypeSymbol entityType)
    {
        entityType = null!;
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

                    if (TryResolveEntityTypeFromBuilderExpression(variable.Initializer.Value, builderVariables, compilationModel, cancellationToken, visitedExpressions, out var localEntityType))
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
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        out INamedTypeSymbol ownedEntity)
    {
        ownedEntity = null!;

        if (memberAccess.Name is GenericNameSyntax genericName)
        {
            var typeArg = genericName.TypeArgumentList.Arguments.FirstOrDefault();
            if (typeArg != null)
            {
                var resolvedOwnedType = compilationModel.FindTypeByName(typeArg.ToString(), cancellationToken);
                if (resolvedOwnedType != null)
                {
                    ownedEntity = resolvedOwnedType;
                    return true;
                }
            }
        }

        if (!TryResolveEntityTypeFromBuilderExpression(memberAccess.Expression, builderVariables, compilationModel, cancellationToken, out var ownerEntity))
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
