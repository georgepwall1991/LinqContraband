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
    private HazardousParameterSummary GetMethodSummary(IMethodSymbol method, HashSet<ISymbol> visiting)
    {
        if (_methodSummaries.TryGetValue(method, out var cachedSummary))
            return cachedSummary;

        if (!visiting.Add(method))
            return HazardousParameterSummary.EmptyInspectable;

        try
        {
            var summary = AnalyzeMethod(method, visiting);
            _methodSummaries.TryAdd(method, summary);
            return summary;
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    private HazardousParameterSummary AnalyzeMethod(IMethodSymbol method, HashSet<ISymbol> visiting)
    {
        if (!TryGetExecutableRoot(method, out var executableRoot))
            return HazardousParameterSummary.NotInspectable;

        var candidateOrdinals = ImmutableHashSet.CreateBuilder<int>();
        foreach (var parameter in method.Parameters)
        {
            if (IsIEnumerableParameterType(parameter.Type) && !IsIQueryableType(parameter.Type))
                candidateOrdinals.Add(parameter.Ordinal);
        }

        if (candidateOrdinals.Count == 0)
            return HazardousParameterSummary.EmptyInspectable;

        var hazardousOrdinals = ImmutableHashSet.CreateBuilder<int>();

        foreach (var operation in EnumerateOperations(executableRoot))
        {
            switch (operation)
            {
                case IForEachLoopOperation forEachLoop:
                    MarkHazardIfParameterSource(
                        forEachLoop.Collection,
                        forEachLoop.Syntax.SpanStart,
                        executableRoot,
                        candidateOrdinals,
                        hazardousOrdinals);
                    break;

                case IInvocationOperation invocation:
                    if (IsDirectConsumption(invocation))
                    {
                        MarkHazardIfParameterSource(
                            invocation.GetInvocationReceiver(),
                            invocation.Syntax.SpanStart,
                            executableRoot,
                            candidateOrdinals,
                            hazardousOrdinals);
                    }

                    MarkForwardedHazards(invocation, executableRoot, candidateOrdinals, hazardousOrdinals, visiting);
                    break;

                case IObjectCreationOperation objectCreation:
                    MarkMaterializingConstructorHazards(
                        objectCreation,
                        executableRoot,
                        candidateOrdinals,
                        hazardousOrdinals);
                    break;
            }
        }

        return new HazardousParameterSummary(true, hazardousOrdinals.ToImmutable());
    }

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

    private void MarkForwardedHazards(
        IInvocationOperation invocation,
        IOperation executableRoot,
        ImmutableHashSet<int>.Builder candidateOrdinals,
        ImmutableHashSet<int>.Builder hazardousOrdinals,
        HashSet<ISymbol> visiting)
    {
        var targetMethod = GetOriginalTargetMethod(invocation.TargetMethod);
        if (targetMethod.MethodKind == MethodKind.DelegateInvoke || targetMethod.IsFrameworkMethod())
            return;

        var calleeSummary = GetMethodSummary(targetMethod, visiting);
        if (!calleeSummary.IsInspectable || calleeSummary.HazardousParameterOrdinals.Count == 0)
            return;

        foreach (var input in EnumerateInvocationInputs(invocation))
        {
            if (!calleeSummary.HazardousParameterOrdinals.Contains(input.Parameter.Ordinal))
                continue;

            if (!TryResolveParameterSource(
                    input.Value,
                    invocation.Syntax.SpanStart,
                    executableRoot,
                    new HashSet<ISymbol>(SymbolEqualityComparer.Default),
                    out var parameter))
            {
                continue;
            }

            if (candidateOrdinals.Contains(parameter.Ordinal))
                hazardousOrdinals.Add(parameter.Ordinal);
        }
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
