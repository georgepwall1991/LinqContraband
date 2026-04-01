using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC004_IQueryableLeak;

internal static class IQueryableLeakDiagnosticProperties
{
    public const string FixerEligible = "LC004.FixerEligible";
}

internal sealed partial class IQueryableLeakCompilationState
{
    private static readonly ImmutableHashSet<string> HazardousEnumerableMethods = ImmutableHashSet.Create(
        "Aggregate",
        "All",
        "Any",
        "Average",
        "Contains",
        "Count",
        "ElementAt",
        "ElementAtOrDefault",
        "First",
        "FirstOrDefault",
        "Last",
        "LastOrDefault",
        "LongCount",
        "Max",
        "MaxBy",
        "Min",
        "MinBy",
        "SequenceEqual",
        "Single",
        "SingleOrDefault",
        "Sum",
        "ToArray",
        "ToDictionary",
        "ToHashSet",
        "ToList",
        "ToLookup");

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _enumerableType;
    private readonly INamedTypeSymbol _enumerableGenericType;
    private readonly INamedTypeSymbol? _queryableType;
    private readonly INamedTypeSymbol? _queryableGenericType;
    private readonly INamedTypeSymbol? _linqEnumerableType;
    private readonly INamedTypeSymbol? _linqQueryableType;
    private readonly ConcurrentDictionary<ISymbol, HazardousParameterSummary> _methodSummaries = new(SymbolEqualityComparer.Default);

    public IQueryableLeakCompilationState(Compilation compilation)
    {
        _compilation = compilation;
        _enumerableType = compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable);
        _enumerableGenericType = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        _queryableType = compilation.GetTypeByMetadataName("System.Linq.IQueryable");
        _queryableGenericType = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
        _linqEnumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        _linqQueryableType = compilation.GetTypeByMetadataName("System.Linq.Queryable");
    }

    public bool CanAnalyze =>
        _enumerableType.SpecialType == SpecialType.System_Collections_IEnumerable &&
        _enumerableGenericType.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
        _linqEnumerableType != null;

    public void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var targetMethod = GetOriginalTargetMethod(invocation.TargetMethod);

        if (targetMethod.MethodKind == MethodKind.DelegateInvoke || targetMethod.IsFrameworkMethod())
            return;

        var summary = GetMethodSummary(targetMethod, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        if (!summary.IsInspectable || summary.HazardousParameterOrdinals.Count == 0)
            return;

        foreach (var input in EnumerateInvocationInputs(invocation))
        {
            if (!summary.HazardousParameterOrdinals.Contains(input.Parameter.Ordinal))
                continue;

            if (!IsIEnumerableParameterType(input.Parameter.Type) || IsIQueryableType(input.Parameter.Type))
                continue;

            if (!TryGetQuerySourceType(input.Value, out var querySourceType))
                continue;

            var properties = ImmutableDictionary<string, string?>.Empty.Add(
                IQueryableLeakDiagnosticProperties.FixerEligible,
                CanOfferToListFix(querySourceType) ? "true" : "false");

            context.ReportDiagnostic(
                Diagnostic.Create(
                    IQueryableLeakAnalyzer.Rule,
                    input.Value.Syntax.GetLocation(),
                    properties,
                    input.Parameter.Name,
                    input.Parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private readonly struct InvocationInput
    {
        public InvocationInput(IOperation value, IParameterSymbol parameter)
        {
            Value = value;
            Parameter = parameter;
        }

        public IOperation Value { get; }
        public IParameterSymbol Parameter { get; }
    }

    private sealed class HazardousParameterSummary
    {
        public HazardousParameterSummary(bool isInspectable, ImmutableHashSet<int> hazardousParameterOrdinals)
        {
            IsInspectable = isInspectable;
            HazardousParameterOrdinals = hazardousParameterOrdinals;
        }

        public bool IsInspectable { get; }
        public ImmutableHashSet<int> HazardousParameterOrdinals { get; }

        public static readonly HazardousParameterSummary NotInspectable = new(false, ImmutableHashSet<int>.Empty);
        public static readonly HazardousParameterSummary EmptyInspectable = new(true, ImmutableHashSet<int>.Empty);
    }
}
