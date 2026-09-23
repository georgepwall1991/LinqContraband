using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    // ApplyConfigurationsFromAssembly(typeof(X).Assembly) applies every constructible
    // IEntityTypeConfiguration<T> in that assembly. When the assembly is this compilation, the
    // configurations are all in source, so each one goes through the same proof as ApplyConfiguration.
    private static bool TryApplyAssemblyConfigurationAutoIncludes(
        IInvocationOperation invocation,
        IParameterSymbol modelBuilderParameter,
        Compilation compilation,
        Dictionary<INamedTypeSymbol, HashSet<string>> prefixesByEntity,
        CancellationToken cancellationToken
    )
    {
        if (
            invocation.TargetMethod.Name != "ApplyConfigurationsFromAssembly"
            || invocation.TargetMethod.ContainingType.Name != "ModelBuilder"
            || invocation.TargetMethod.ContainingType.ContainingNamespace.ToDisplayString()
                != "Microsoft.EntityFrameworkCore"
            || invocation.Instance?.UnwrapConversions()
                is not IParameterReferenceOperation modelBuilderReference
            || !SymbolEqualityComparer.Default.Equals(
                modelBuilderReference.Parameter.OriginalDefinition,
                modelBuilderParameter.OriginalDefinition
            )
            || invocation.Arguments.Length == 0
            || HasExplicitConfigurationPredicate(invocation)
            || !IsCompilationAssembly(invocation.Arguments[0].Value, compilation)
        )
        {
            return false;
        }

        var changesByEntity = new Dictionary<INamedTypeSymbol, List<AppliedAutoIncludeChange>>(
            SymbolEqualityComparer.Default
        );
        foreach (var type in EnumerateConfigurationCandidates(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConstructibleConfigurationType(type))
                continue;

            foreach (var interfaceType in type.AllInterfaces)
            {
                if (
                    interfaceType.Name != "IEntityTypeConfiguration"
                    || interfaceType.ContainingNamespace.ToDisplayString()
                        != "Microsoft.EntityFrameworkCore"
                    || interfaceType.TypeArguments.Length != 1
                )
                {
                    continue;
                }

                if (
                    interfaceType.TypeArguments[0] is not INamedTypeSymbol entityType
                    || !TryCollectAppliedConfigurationChanges(
                        type,
                        entityType,
                        compilation,
                        cancellationToken,
                        out var changes
                    )
                )
                {
                    return false;
                }

                if (changes.Count == 0)
                    continue;

                // EF applies the configurations in type-name order. Two configurations that both
                // change one entity's auto-includes would need that order, which this proof skips.
                if (changesByEntity.ContainsKey(entityType))
                    return false;

                changesByEntity[entityType] = changes;
            }
        }

        foreach (var pair in changesByEntity)
            ApplyAutoIncludeChanges(pair.Key, pair.Value, prefixesByEntity);

        return true;
    }

    private static bool HasExplicitConfigurationPredicate(IInvocationOperation invocation)
    {
        for (var i = 1; i < invocation.Arguments.Length; i++)
        {
            if (!invocation.Arguments[i].IsImplicit)
                return true;
        }

        return false;
    }

    private static bool IsCompilationAssembly(IOperation argument, Compilation compilation)
    {
        switch (argument.UnwrapConversions())
        {
            case IPropertyReferenceOperation { Property.Name: "Assembly" } property:
                return property.Property.ContainingType.ToDisplayString() == "System.Type"
                    && property.Instance?.UnwrapConversions()
                        is ITypeOfOperation { TypeOperand: INamedTypeSymbol typeOperand }
                    && SymbolEqualityComparer.Default.Equals(
                        typeOperand.ContainingAssembly,
                        compilation.Assembly
                    );
            case IInvocationOperation invocation:
                return invocation.TargetMethod.Name == "GetExecutingAssembly"
                    && invocation.TargetMethod.ContainingType.ToDisplayString()
                        == "System.Reflection.Assembly";
            default:
                return false;
        }
    }

    private static bool IsConstructibleConfigurationType(INamedTypeSymbol type)
    {
        // Mirrors EF Core: a non-abstract, non-generic class with a public parameterless constructor.
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
            return false;

        foreach (var constructor in type.InstanceConstructors)
        {
            if (
                constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility == Accessibility.Public
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateConfigurationCandidates(
        INamespaceSymbol namespaceSymbol
    )
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var candidate in EnumerateTypeAndNested(type))
                yield return candidate;
        }

        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in EnumerateConfigurationCandidates(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var candidate in EnumerateTypeAndNested(nested))
                yield return candidate;
        }
    }
}
