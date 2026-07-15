using System.Collections.Concurrent;
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
    private static void AddModelAutoIncludePrefixes(
        QueryChainInfo query,
        ConcurrentDictionary<
            INamedTypeSymbol,
            Dictionary<INamedTypeSymbol, HashSet<string>>
        > autoIncludeCache,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        if (query.IgnoresAutoIncludes)
            return;

        var byEntity = autoIncludeCache.GetOrAdd(
            query.ContextType,
            contextType =>
                CollectModelAutoIncludePrefixes(contextType, compilation, cancellationToken)
        );
        if (byEntity.TryGetValue(query.EntityType, out var prefixes))
            query.IncludedPrefixes.UnionWith(prefixes);
    }

    private static Dictionary<INamedTypeSymbol, HashSet<string>> CollectModelAutoIncludePrefixes(
        INamedTypeSymbol contextType,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var prefixesByEntity = new Dictionary<INamedTypeSymbol, HashSet<string>>(
            SymbolEqualityComparer.Default
        );

        for (
            INamedTypeSymbol? currentContext = contextType;
            currentContext != null && currentContext.IsDbContext();
            currentContext = currentContext.BaseType
        )
        {
            var methods = currentContext
                .GetMembers("OnModelCreating")
                .OfType<IMethodSymbol>()
                .Where(IsExactOnModelCreating)
                .ToArray();
            if (methods.Length == 0)
                continue;

            foreach (var method in methods)
            {
                foreach (var syntaxReference in method.DeclaringSyntaxReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var syntax = syntaxReference.GetSyntax(cancellationToken);
                    var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);

                    foreach (
                        var invocationSyntax in syntax
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                    )
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (
                            semanticModel.GetOperation(invocationSyntax, cancellationToken)
                            is not IInvocationOperation configurationInvocation
                        )
                        {
                            continue;
                        }

                        var isTopLevel = TryGetTopLevelConfigurationExecution(
                            configurationInvocation,
                            semanticModel,
                            out var isUnconditional
                        );
                        if (
                            !TryGetDirectAutoInclude(
                                configurationInvocation,
                                method.Parameters[0],
                                semanticModel,
                                out var entityType,
                                out var includePath,
                                out var enabled
                            )
                        )
                        {
                            if (
                                isTopLevel
                                && IsUnprovenModelConfigurationBoundary(
                                    configurationInvocation,
                                    method.Parameters[0]
                                )
                            )
                            {
                                prefixesByEntity.Clear();
                            }

                            continue;
                        }

                        if (!prefixesByEntity.TryGetValue(entityType, out var prefixes))
                        {
                            prefixes = new HashSet<string>(System.StringComparer.Ordinal);
                            prefixesByEntity[entityType] = prefixes;
                        }

                        if (enabled == true)
                        {
                            if (isTopLevel && isUnconditional)
                                AddPathPrefixes(includePath, prefixes);
                        }
                        else
                        {
                            var disabledPath = includePath.Key;
                            prefixes.RemoveWhere(path =>
                                path == disabledPath
                                || path.StartsWith(
                                    disabledPath + ".",
                                    System.StringComparison.Ordinal
                                )
                            );
                        }
                    }
                }
            }

            // Only the nearest declared override is guaranteed to define this context's model.
            // A base implementation applies only when that override calls it, which this narrow
            // proof deliberately does not infer.
            break;
        }

        return prefixesByEntity;
    }

    private static bool IsExactOnModelCreating(IMethodSymbol method)
    {
        if (!method.IsOverride || method.Parameters.Length != 1)
        {
            return false;
        }

        if (
            method.Parameters[0].Type is not INamedTypeSymbol parameterType
            || parameterType.Name != "ModelBuilder"
            || parameterType.ContainingNamespace.ToDisplayString()
                != "Microsoft.EntityFrameworkCore"
        )
        {
            return false;
        }

        var rootMethod = method.OverriddenMethod;
        while (rootMethod?.OverriddenMethod != null)
            rootMethod = rootMethod.OverriddenMethod;

        return rootMethod?.Name == "OnModelCreating"
            && rootMethod.ContainingType.Name == "DbContext"
            && rootMethod.ContainingType.ContainingNamespace.ToDisplayString()
                == "Microsoft.EntityFrameworkCore";
    }

    private static bool TryGetDirectAutoInclude(
        IInvocationOperation autoInclude,
        IParameterSymbol modelBuilderParameter,
        SemanticModel semanticModel,
        out INamedTypeSymbol entityType,
        out IncludePath includePath,
        out bool? enabled
    )
    {
        entityType = null!;
        includePath = null!;
        enabled = null;

        if (
            autoInclude.TargetMethod.Name != "AutoInclude"
            || !IsEfBuilderType(autoInclude.TargetMethod.ContainingType, "NavigationBuilder")
            || !TryGetAutoIncludeSetting(autoInclude, out enabled)
            || !TryGetNavigationInvocation(autoInclude, out var navigationInvocation)
            || navigationInvocation.TargetMethod.Name != "Navigation"
            || !IsEfBuilderType(
                navigationInvocation.TargetMethod.ContainingType,
                "EntityTypeBuilder"
            )
            || navigationInvocation.Instance?.UnwrapConversions()
                is not IInvocationOperation entityInvocation
            || entityInvocation.TargetMethod.Name != "Entity"
            || entityInvocation.TargetMethod.ContainingType.Name != "ModelBuilder"
            || entityInvocation.TargetMethod.ContainingType.ContainingNamespace.ToDisplayString()
                != "Microsoft.EntityFrameworkCore"
            || entityInvocation.Instance?.UnwrapConversions()
                is not IParameterReferenceOperation modelBuilderReference
            || !SymbolEqualityComparer.Default.Equals(
                modelBuilderReference.Parameter.OriginalDefinition,
                modelBuilderParameter.OriginalDefinition
            )
            || entityInvocation.TargetMethod.TypeArguments.Length != 1
            || entityInvocation.TargetMethod.TypeArguments[0]
                is not INamedTypeSymbol configuredEntity
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

        entityType = configuredEntity;
        return true;
    }

    private static bool TryGetNavigationInvocation(
        IInvocationOperation autoInclude,
        out IInvocationOperation navigationInvocation
    )
    {
        var receiver = autoInclude.Instance?.UnwrapConversions();
        while (
            receiver is IInvocationOperation chainedAutoInclude
            && chainedAutoInclude.TargetMethod.Name == "AutoInclude"
            && IsEfBuilderType(chainedAutoInclude.TargetMethod.ContainingType, "NavigationBuilder")
        )
        {
            receiver = chainedAutoInclude.Instance?.UnwrapConversions();
        }

        if (receiver is not IInvocationOperation navigation)
        {
            navigationInvocation = null!;
            return false;
        }

        navigationInvocation = navigation;
        return true;
    }

    private static bool TryGetAutoIncludeSetting(IInvocationOperation invocation, out bool? enabled)
    {
        enabled = null;
        var argument = invocation.Arguments.FirstOrDefault(item => item.Parameter?.Ordinal == 0);
        if (argument != null)
        {
            if (argument.Value.ConstantValue is not { HasValue: true, Value: bool constantValue })
            {
                return true;
            }

            enabled = constantValue;
            return true;
        }

        if (
            invocation.TargetMethod.Parameters.Length != 1
            || !invocation.TargetMethod.Parameters[0].HasExplicitDefaultValue
            || invocation.TargetMethod.Parameters[0].ExplicitDefaultValue is not bool defaultValue
        )
        {
            return false;
        }

        enabled = defaultValue;
        return true;
    }

    private static bool TryGetTopLevelConfigurationExecution(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        out bool isUnconditional
    )
    {
        isUnconditional = false;
        var statement = invocation.Syntax.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (
            statement?.Parent is BlockSyntax block
            && block.Parent is MethodDeclarationSyntax method
            && method.Body == block
            && statement.Expression == invocation.Syntax
        )
        {
            var statementIndex = block.Statements.IndexOf(statement);
            if (statementIndex == 0)
            {
                isUnconditional = true;
                return true;
            }

            var precedingFlow = semanticModel.AnalyzeControlFlow(
                block.Statements[0],
                block.Statements[statementIndex - 1]
            );
            isUnconditional = precedingFlow.Succeeded && precedingFlow.ExitPoints.Length == 0;
            return true;
        }

        var arrow = invocation.Syntax.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>();
        if (arrow?.Parent is MethodDeclarationSyntax && arrow.Expression == invocation.Syntax)
        {
            isUnconditional = true;
            return true;
        }

        return false;
    }

    private static bool IsUnprovenModelConfigurationBoundary(
        IInvocationOperation invocation,
        IParameterSymbol modelBuilderParameter
    )
    {
        return invocation.TargetMethod.Name
                is "AutoInclude"
                    or "OnModelCreating"
                    or "ApplyConfiguration"
                    or "ApplyConfigurationsFromAssembly"
            || ReferencesParameter(invocation.Instance, modelBuilderParameter)
            || invocation.Arguments.Any(argument =>
                ReferencesParameter(argument.Value, modelBuilderParameter)
            );
    }

    private static bool ReferencesParameter(IOperation? operation, IParameterSymbol parameter)
    {
        if (operation == null)
            return false;

        if (
            operation is IParameterReferenceOperation parameterReference
            && SymbolEqualityComparer.Default.Equals(
                parameterReference.Parameter.OriginalDefinition,
                parameter.OriginalDefinition
            )
        )
        {
            return true;
        }

        return operation.ChildOperations.Any(child => ReferencesParameter(child, parameter));
    }

    private static bool IsEfBuilderType(INamedTypeSymbol type, string expectedName)
    {
        return type.Name == expectedName
            && type.ContainingNamespace.ToDisplayString()
                == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }
}
