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
            if (!ImplementsEntityTypeConfiguration(type))
                continue;

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
        for (var current = configurationType; current != null; current = current.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.SpecialType == SpecialType.System_Object)
                break;

            foreach (var member in current.GetMembers("Configure"))
            {
                if (member is not IMethodSymbol method)
                    continue;

                ScanCascadeMethodTree(method, current, evidenceContext, visited, depth, cancellationToken);
            }
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
            case IConversionOperation conversion:
                foreach (var type in GetArgumentConfigurationTypes(conversion.Operand, executableRoot, cancellationToken))
                    yield return type;
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
        var configInterface = compilation.GetTypeByMetadataName(
            "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (configInterface == null)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, configInterface))
                return true;
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
