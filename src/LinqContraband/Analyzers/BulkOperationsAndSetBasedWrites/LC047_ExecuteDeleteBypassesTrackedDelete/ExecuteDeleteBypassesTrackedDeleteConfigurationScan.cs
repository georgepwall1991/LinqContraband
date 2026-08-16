using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

internal sealed partial class TrackedDeletePipelineEvidence
{
    private void ScanAppliedConfigurationInvocation(
        IInvocationOperation invocation,
        IOperation executableRoot,
        INamedTypeSymbol evidenceContext,
        HashSet<IMethodSymbol> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (!IsEfModelBuilderMethod(invocation.TargetMethod, "ApplyConfiguration"))
            return;

        foreach (var argument in invocation.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var configurationType in GetArgumentConfigurationTypes(
                         argument.Value,
                         executableRoot,
                         cancellationToken))
            {
                if (!ImplementsEntityTypeConfiguration(configurationType))
                    continue;

                ScanEntityTypeConfiguration(
                    configurationType,
                    evidenceContext,
                    visited,
                    depth + 1,
                    cancellationToken);
            }
        }
    }

    private void ScanAppliedConfigurationsFromAssembly(
        IInvocationOperation invocation,
        IOperation executableRoot,
        INamedTypeSymbol evidenceContext,
        HashSet<IMethodSymbol> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (!IsEfModelBuilderMethod(invocation.TargetMethod, "ApplyConfigurationsFromAssembly") ||
            invocation.Arguments.Length == 0 ||
            HasExplicitAssemblyPredicate(invocation) ||
            !IsCurrentCompilationAssembly(
                invocation.Arguments[0].Value,
                executableRoot,
                cancellationToken))
        {
            return;
        }

        foreach (var type in EnumerateSourceTypes(compilation.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ImplementsEntityTypeConfiguration(type) ||
                !IsConstructibleEntityTypeConfiguration(type))
            {
                continue;
            }

            ScanEntityTypeConfiguration(type, evidenceContext, visited, depth + 1, cancellationToken);
        }
    }

    private void ScanEntityTypeConfiguration(
        INamedTypeSymbol configurationType,
        INamedTypeSymbol evidenceContext,
        HashSet<IMethodSymbol> visited,
        int depth,
        CancellationToken cancellationToken)
    {
        if (entityTypeConfigurationInterface == null)
            return;

        foreach (var iface in configurationType.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, entityTypeConfigurationInterface))
                continue;

            IMethodSymbol? interfaceMethod = null;
            foreach (var member in iface.GetMembers("Configure"))
            {
                if (member is IMethodSymbol method)
                {
                    interfaceMethod = method;
                    break;
                }
            }

            if (interfaceMethod == null)
                continue;

            if (configurationType.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation)
                continue;

            implementation = GetMostDerivedOverride(configurationType, implementation);
            var helperOwner = implementation.ContainingType as INamedTypeSymbol ?? configurationType;
            ScanCascadeMethodTree(implementation, helperOwner, evidenceContext, visited, depth, cancellationToken);
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetArgumentConfigurationTypes(
        IOperation argument,
        IOperation executableRoot,
        CancellationToken cancellationToken)
    {
        var current = argument.UnwrapConversions();
        switch (current)
        {
            case IObjectCreationOperation creation when creation.Type is INamedTypeSymbol created:
                yield return created;
                yield break;
            case ILocalReferenceOperation local:
                if (LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                        executableRoot,
                        local.Local,
                        local.Syntax.SpanStart,
                        out var assigned,
                        cancellationToken))
                {
                    foreach (var type in GetArgumentConfigurationTypes(assigned, executableRoot, cancellationToken))
                        yield return type;
                }

                yield break;
            default:
                yield break;
        }
    }

    private bool IsCurrentCompilationAssembly(
        IOperation argument,
        IOperation executableRoot,
        CancellationToken cancellationToken)
    {
        var current = argument.UnwrapConversions();
        switch (current)
        {
            case IPropertyReferenceOperation property when property.Property.Name == "Assembly":
                var instance = property.Instance?.UnwrapConversions();
                if (instance is not ITypeOfOperation typeOf || typeOf.TypeOperand == null)
                    return false;

                return SymbolEqualityComparer.Default.Equals(
                    typeOf.TypeOperand.ContainingAssembly,
                    compilation.Assembly);
            case IInvocationOperation invocation
                when invocation.TargetMethod.Name == "GetExecutingAssembly" &&
                     invocation.TargetMethod.ContainingType?.ToString() == "System.Reflection.Assembly" &&
                     invocation.TargetMethod.ContainingType.ContainingNamespace?.ToString() == "System.Reflection":
                return true;
            case ILocalReferenceOperation local:
                return LocalAssignmentCache.TryGetSingleAssignedValueBefore(
                           executableRoot,
                           local.Local,
                           local.Syntax.SpanStart,
                           out var assigned,
                           cancellationToken) &&
                       IsCurrentCompilationAssembly(assigned, executableRoot, cancellationToken);
            default:
                return false;
        }
    }

    private bool ImplementsEntityTypeConfiguration(INamedTypeSymbol type)
    {
        if (entityTypeConfigurationInterface == null)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, entityTypeConfigurationInterface))
                return true;
        }

        return false;
    }

    private static IMethodSymbol GetMostDerivedOverride(INamedTypeSymbol type, IMethodSymbol method)
    {
        for (var currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            foreach (var member in currentType.GetMembers(method.Name))
            {
                if (member is not IMethodSymbol candidate)
                    continue;

                for (var walk = candidate; walk != null; walk = walk.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(walk, method) ||
                        SymbolEqualityComparer.Default.Equals(walk.OriginalDefinition, method.OriginalDefinition))
                    {
                        return candidate;
                    }
                }
            }
        }

        return method;
    }

    private static bool HasExplicitAssemblyPredicate(IInvocationOperation invocation)
    {
        for (var i = 1; i < invocation.Arguments.Length; i++)
        {
            if (!invocation.Arguments[i].IsImplicit)
                return true;
        }

        return false;
    }

    private static bool IsConstructibleEntityTypeConfiguration(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsUnboundGenericType)
            return false;

        foreach (var argument in type.TypeArguments)
        {
            if (argument.TypeKind == TypeKind.TypeParameter)
                return false;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEfModelBuilderMethod(IMethodSymbol method, string name)
    {
        return method.Name == name &&
               method.ContainingType.Name == "ModelBuilder" &&
               method.ContainingType.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";
    }
}
