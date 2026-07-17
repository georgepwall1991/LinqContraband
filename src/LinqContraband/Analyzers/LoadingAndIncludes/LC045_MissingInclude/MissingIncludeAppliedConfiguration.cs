using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    private static bool TryApplyConfigurationAutoIncludes(
        IInvocationOperation invocation,
        IParameterSymbol modelBuilderParameter,
        Compilation compilation,
        Dictionary<INamedTypeSymbol, HashSet<string>> prefixesByEntity,
        CancellationToken cancellationToken
    )
    {
        if (
            invocation.TargetMethod.Name != "ApplyConfiguration"
            || invocation.TargetMethod.ContainingType.Name != "ModelBuilder"
            || invocation.TargetMethod.ContainingType.ContainingNamespace.ToDisplayString()
                != "Microsoft.EntityFrameworkCore"
            || invocation.Instance?.UnwrapConversions()
                is not IParameterReferenceOperation modelBuilderReference
            || !SymbolEqualityComparer.Default.Equals(
                modelBuilderReference.Parameter.OriginalDefinition,
                modelBuilderParameter.OriginalDefinition
            )
            || invocation.TargetMethod.TypeArguments.Length != 1
            || invocation.TargetMethod.TypeArguments[0] is not INamedTypeSymbol entityType
        )
        {
            return false;
        }

        var configurationArgument = invocation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Ordinal == 0
        );
        if (
            configurationArgument?.Value.UnwrapConversions()
                is not IObjectCreationOperation configurationCreation
            || configurationCreation.Type is not INamedTypeSymbol configurationType
            || !TryGetExactConfigureMethod(
                configurationType,
                entityType,
                out var configureMethod
            )
            || configureMethod.Parameters.Length != 1
            || configureMethod.Parameters[0].Type is not INamedTypeSymbol builderType
            || !IsEfBuilderType(builderType, "EntityTypeBuilder")
        )
        {
            return false;
        }

        var changes = new List<AppliedAutoIncludeChange>();
        foreach (var syntaxReference in configureMethod.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            if (
                HasEscapedConfigurationBuilder(
                    syntax,
                    semanticModel,
                    configureMethod.Parameters[0],
                    cancellationToken
                )
            )
            {
                return false;
            }

            foreach (
                var invocationSyntax in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    semanticModel.GetOperation(invocationSyntax, cancellationToken)
                        is not IInvocationOperation configurationInvocation
                )
                {
                    return false;
                }

                if (
                    IsNestedConfigurationExecutable(invocationSyntax, syntax)
                    && (ReferencesParameter(
                            configurationInvocation,
                            configureMethod.Parameters[0],
                            cancellationToken
                        )
                        || UsesEfConfigurationBuilder(configurationInvocation))
                )
                {
                    return false;
                }

                var isTopLevel = TryGetTopLevelConfigurationExecution(
                    configurationInvocation,
                    semanticModel,
                    out var isUnconditional
                );
                if (
                    TryGetAppliedConfigurationAutoInclude(
                        configurationInvocation,
                        configureMethod.Parameters[0],
                        entityType,
                        semanticModel,
                        out var includePath,
                        out var enabled
                    )
                )
                {
                    if (enabled == true)
                    {
                        if (isTopLevel && isUnconditional)
                            changes.Add(new AppliedAutoIncludeChange(includePath, true));
                    }
                    else
                    {
                        changes.Add(new AppliedAutoIncludeChange(includePath, false));
                    }

                    continue;
                }

                if (
                    IsUnprovenAppliedConfigurationBoundary(
                        configurationInvocation,
                        configureMethod.Parameters[0],
                        cancellationToken
                    )
                )
                {
                    return false;
                }
            }
        }

        if (!prefixesByEntity.TryGetValue(entityType, out var prefixes))
        {
            prefixes = new HashSet<string>(System.StringComparer.Ordinal);
            prefixesByEntity[entityType] = prefixes;
        }

        foreach (var change in changes)
        {
            if (change.Enabled)
            {
                AddPathPrefixes(change.Path, prefixes);
                continue;
            }

            var disabledPath = change.Path.Key;
            prefixes.RemoveWhere(path =>
                path == disabledPath
                || path.StartsWith(disabledPath + ".", System.StringComparison.Ordinal)
            );
        }

        return true;
    }

    private static bool HasEscapedConfigurationBuilder(
        SyntaxNode configureSyntax,
        SemanticModel semanticModel,
        IParameterSymbol builderParameter,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var assignmentSyntax in configureSyntax
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                semanticModel.GetOperation(assignmentSyntax, cancellationToken)
                    is IAssignmentOperation assignment
                && assignment.Target.UnwrapConversions()
                    is not (ILocalReferenceOperation or IDiscardOperation)
                && (ReferencesParameter(
                        assignment.Value,
                        builderParameter,
                        cancellationToken
                    )
                    || ReferencesParameter(
                        assignment.Target,
                        builderParameter,
                        cancellationToken
                    ))
            )
            {
                return true;
            }
        }

        foreach (
            var objectCreationSyntax in configureSyntax
                .DescendantNodes()
                .OfType<BaseObjectCreationExpressionSyntax>()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                semanticModel.GetOperation(objectCreationSyntax, cancellationToken)
                    is IObjectCreationOperation objectCreation
                && objectCreation.Arguments.Any(argument =>
                    ReferencesParameter(
                        argument.Value,
                        builderParameter,
                        cancellationToken
                    )
                    || IsEfConfigurationBuilderType(argument.Value.Type)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNestedConfigurationExecutable(
        SyntaxNode invocationSyntax,
        SyntaxNode configureSyntax
    )
    {
        return invocationSyntax
            .Ancestors()
            .TakeWhile(ancestor => ancestor != configureSyntax)
            .Any(ancestor =>
                ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax
            );
    }

    private static bool UsesEfConfigurationBuilder(IInvocationOperation invocation)
    {
        return IsEfConfigurationBuilderType(invocation.TargetMethod.ContainingType)
            || IsEfConfigurationBuilderType(invocation.Instance?.Type)
            || invocation.Arguments.Any(argument =>
                IsEfConfigurationBuilderType(argument.Value.Type)
            );
    }

    private static bool IsEfConfigurationBuilderType(ITypeSymbol? type)
    {
        return type?.ContainingNamespace.ToDisplayString()
            == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static bool TryGetExactConfigureMethod(
        INamedTypeSymbol configurationType,
        INamedTypeSymbol entityType,
        out IMethodSymbol configureMethod
    )
    {
        configureMethod = null!;
        foreach (var interfaceType in configurationType.AllInterfaces)
        {
            if (
                interfaceType.Name != "IEntityTypeConfiguration"
                || interfaceType.ContainingNamespace.ToDisplayString()
                    != "Microsoft.EntityFrameworkCore"
                || interfaceType.TypeArguments.Length != 1
                || !SymbolEqualityComparer.Default.Equals(
                    interfaceType.TypeArguments[0],
                    entityType
                )
            )
            {
                continue;
            }

            var interfaceMethod = interfaceType
                .GetMembers("Configure")
                .OfType<IMethodSymbol>()
                .SingleOrDefault(method => method.Parameters.Length == 1);
            if (
                interfaceMethod == null
                || configurationType.FindImplementationForInterfaceMember(interfaceMethod)
                    is not IMethodSymbol implementation
                || implementation.DeclaringSyntaxReferences.Length == 0
            )
            {
                return false;
            }

            configureMethod = implementation;
            return true;
        }

        return false;
    }

    private static bool TryGetAppliedConfigurationAutoInclude(
        IInvocationOperation autoInclude,
        IParameterSymbol builderParameter,
        INamedTypeSymbol entityType,
        SemanticModel semanticModel,
        out IncludePath includePath,
        out bool? enabled
    )
    {
        includePath = null!;
        enabled = null;
        if (
            IsChainedAutoIncludeReceiver(autoInclude)
            || autoInclude.TargetMethod.Name != "AutoInclude"
            || !IsEfBuilderType(autoInclude.TargetMethod.ContainingType, "NavigationBuilder")
            || !TryGetAutoIncludeSetting(autoInclude, out enabled)
            || !TryGetNavigationInvocation(autoInclude, out var navigationInvocation)
            || navigationInvocation.TargetMethod.Name != "Navigation"
            || !IsEfBuilderType(
                navigationInvocation.TargetMethod.ContainingType,
                "EntityTypeBuilder"
            )
            || navigationInvocation.Instance?.UnwrapConversions()
                is not IParameterReferenceOperation builderReference
            || !SymbolEqualityComparer.Default.Equals(
                builderReference.Parameter.OriginalDefinition,
                builderParameter.OriginalDefinition
            )
            || builderParameter.Type is not INamedTypeSymbol configuredBuilder
            || configuredBuilder.TypeArguments.Length != 1
            || !SymbolEqualityComparer.Default.Equals(
                configuredBuilder.TypeArguments[0],
                entityType
            )
            || !IncludePathParser.TryGetIncludePath(
                navigationInvocation,
                semanticModel,
                null,
                out includePath
            )
        )
        {
            return false;
        }

        return true;
    }

    private static bool IsUnprovenAppliedConfigurationBoundary(
        IInvocationOperation invocation,
        IParameterSymbol builderParameter,
        CancellationToken cancellationToken
    )
    {
        if (IsChainedAutoIncludeReceiver(invocation))
            return false;

        if (
            !ReferencesParameter(invocation, builderParameter, cancellationToken)
            || IsKnownEfBuilderOperation(invocation.TargetMethod, builderParameter)
        )
        {
            return false;
        }

        return true;
    }

    private static bool IsKnownEfBuilderOperation(
        IMethodSymbol method,
        IParameterSymbol builderParameter
    )
    {
        var containingType = method.ContainingType;
        var containingNamespace = containingType.ContainingNamespace.ToDisplayString();
        var builderAssembly = builderParameter.Type.ContainingAssembly;
        if (
            containingNamespace == "Microsoft.EntityFrameworkCore.Metadata.Builders"
            && SymbolEqualityComparer.Default.Equals(
                containingType.ContainingAssembly,
                builderAssembly
            )
            && containingType.Name == "EntityTypeBuilder"
            && method.Name == "HasKey"
        )
        {
            return true;
        }

        return method.Name == "ToTable"
            && containingType.Name == "RelationalEntityTypeBuilderExtensions"
            && containingNamespace == "Microsoft.EntityFrameworkCore"
            && (containingType.ContainingAssembly.Name == "Microsoft.EntityFrameworkCore.Relational"
                || SymbolEqualityComparer.Default.Equals(
                    containingType.ContainingAssembly,
                    builderAssembly
                ));
    }

    private readonly struct AppliedAutoIncludeChange
    {
        public AppliedAutoIncludeChange(IncludePath path, bool enabled)
        {
            Path = path;
            Enabled = enabled;
        }

        public IncludePath Path { get; }

        public bool Enabled { get; }
    }
}
