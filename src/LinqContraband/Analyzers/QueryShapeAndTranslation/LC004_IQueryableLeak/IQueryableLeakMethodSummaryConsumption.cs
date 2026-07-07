using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal sealed partial class IQueryableLeakCompilationState
{
    private void MarkMaterializingConstructorHazards(
        IObjectCreationOperation objectCreation,
        IOperation executableRoot,
        ImmutableHashSet<int>.Builder candidateOrdinals,
        ImmutableHashSet<int>.Builder hazardousOrdinals)
    {
        if (!IsMaterializingCollectionConstructor(objectCreation.Constructor))
            return;

        foreach (var argument in objectCreation.Arguments)
        {
            if (argument.Parameter == null || !IsIEnumerableLike(argument.Parameter.Type))
                continue;

            MarkHazardIfParameterSource(
                argument.Value,
                objectCreation.Syntax.SpanStart,
                executableRoot,
                candidateOrdinals,
                hazardousOrdinals);
        }
    }

    private bool IsMaterializingCollectionConstructor(IMethodSymbol? constructor)
    {
        if (constructor == null || constructor.MethodKind != MethodKind.Constructor)
            return false;

        var containingType = constructor.ContainingType;
        if (containingType?.ContainingNamespace?.ToString() != "System.Collections.Generic")
            return false;

        return MaterializingCollectionTypes.Contains(containingType.Name) &&
               constructor.Parameters.Any(parameter => IsIEnumerableLike(parameter.Type));
    }

    private bool IsDirectConsumption(IInvocationOperation invocation)
    {
        if (IsGetEnumeratorInvocation(invocation))
            return true;

        var targetMethod = GetOriginalTargetMethod(invocation.TargetMethod);
        return IsEnumerableMethod(targetMethod) &&
               HazardousEnumerableMethods.Contains(targetMethod.Name);
    }

    private bool IsGetEnumeratorInvocation(IInvocationOperation invocation)
    {
        return invocation.Arguments.Length == 0 &&
               string.Equals(invocation.TargetMethod.Name, "GetEnumerator", StringComparison.Ordinal) &&
               IsIEnumerableLike(invocation.GetInvocationReceiver()?.Type);
    }

    private void MarkHazardIfParameterSource(
        IOperation? operation,
        int position,
        IOperation executableRoot,
        ImmutableHashSet<int>.Builder candidateOrdinals,
        ImmutableHashSet<int>.Builder hazardousOrdinals)
    {
        if (!TryResolveParameterSource(
                operation,
                position,
                executableRoot,
                new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                out var parameter))
        {
            return;
        }

        if (candidateOrdinals.Contains(parameter.Ordinal))
            hazardousOrdinals.Add(parameter.Ordinal);
    }
}
