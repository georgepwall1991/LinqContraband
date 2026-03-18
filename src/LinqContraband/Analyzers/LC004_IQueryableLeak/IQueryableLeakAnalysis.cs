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

internal sealed class IQueryableLeakCompilationState
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
            }
        }

        return new HazardousParameterSummary(true, hazardousOrdinals.ToImmutable());
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

    private bool TryResolveParameterSource(
        IOperation? operation,
        int position,
        IOperation executableRoot,
        HashSet<ISymbol> visitedLocals,
        out IParameterSymbol parameter)
    {
        parameter = null!;
        if (operation == null)
            return false;

        var current = operation.UnwrapConversions();

        if (current is IParameterReferenceOperation parameterReference)
        {
            parameter = parameterReference.Parameter;
            return true;
        }

        if (current is ILocalReferenceOperation localReference)
        {
            if (!visitedLocals.Add(localReference.Local))
                return false;

            if (!TryResolveSingleAssignedValue(executableRoot, localReference.Local, position, out var assignedValue))
                return false;

            return TryResolveParameterSource(assignedValue, position, executableRoot, visitedLocals, out parameter);
        }

        if (current is IInvocationOperation invocation &&
            IsSequencePreservingInvocation(invocation))
        {
            return TryResolveParameterSource(
                invocation.GetInvocationReceiver(),
                position,
                executableRoot,
                visitedLocals,
                out parameter);
        }

        return false;
    }

    private bool TryResolveSingleAssignedValue(
        IOperation executableRoot,
        ILocalSymbol local,
        int position,
        out IOperation value)
    {
        value = null!;
        IOperation? latestValue = null;
        var latestPosition = -1;
        var assignmentCount = 0;

        foreach (var operation in EnumerateOperations(executableRoot))
        {
            if (operation.Syntax.SpanStart >= position)
                continue;

            switch (operation)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null &&
                         declarator.Syntax.SpanStart > latestPosition:
                    latestValue = declarator.Initializer.Value;
                    latestPosition = declarator.Syntax.SpanStart;
                    assignmentCount++;
                    break;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation targetLocal &&
                         SymbolEqualityComparer.Default.Equals(targetLocal.Local, local) &&
                         assignment.Syntax.SpanStart > latestPosition:
                    latestValue = assignment.Value;
                    latestPosition = assignment.Syntax.SpanStart;
                    assignmentCount++;
                    break;
            }
        }

        if (latestValue == null || assignmentCount != 1)
            return false;

        value = latestValue.UnwrapConversions();
        return true;
    }

    private IEnumerable<InvocationInput> EnumerateInvocationInputs(IInvocationOperation invocation)
    {
        var targetMethod = invocation.TargetMethod;
        var originalTargetMethod = GetOriginalTargetMethod(targetMethod);

        if (targetMethod.ReducedFrom != null && invocation.Instance != null && originalTargetMethod.Parameters.Length > 0)
        {
            yield return new InvocationInput(invocation.Instance, originalTargetMethod.Parameters[0]);
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter == null)
                continue;

            var parameter = argument.Parameter;
            if (targetMethod.ReducedFrom != null)
            {
                var originalOrdinal = parameter.Ordinal + 1;
                if (originalOrdinal >= originalTargetMethod.Parameters.Length)
                    continue;

                parameter = originalTargetMethod.Parameters[originalOrdinal];
            }

            yield return new InvocationInput(argument.Value, parameter);
        }
    }

    private bool TryGetQuerySourceType(IOperation operation, out ITypeSymbol sourceType)
    {
        sourceType = null!;
        var current = operation.UnwrapConversions();
        if (current.Type == null || !IsIQueryableType(current.Type))
            return false;

        sourceType = current.Type;
        return true;
    }

    private bool CanOfferToListFix(ITypeSymbol sourceType)
    {
        return TryGetConstructedInterface(sourceType, _queryableGenericType, out _);
    }

    private bool IsSequencePreservingInvocation(IInvocationOperation invocation)
    {
        var targetMethod = GetOriginalTargetMethod(invocation.TargetMethod);
        if (!IsEnumerableMethod(targetMethod) && !IsQueryableMethod(targetMethod))
            return false;

        if (!IsIEnumerableLike(targetMethod.ReturnType) && !IsIQueryableType(targetMethod.ReturnType))
            return false;

        return invocation.GetInvocationReceiver() != null;
    }

    private bool IsEnumerableMethod(IMethodSymbol method)
    {
        return _linqEnumerableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqEnumerableType);
    }

    private bool IsQueryableMethod(IMethodSymbol method)
    {
        return _linqQueryableType != null &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, _linqQueryableType);
    }

    private bool IsIEnumerableParameterType(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _enumerableType) ||
               TryGetConstructedInterface(type, _enumerableGenericType, out var enumerableInterface) &&
               SymbolEqualityComparer.Default.Equals(type, enumerableInterface);
    }

    private bool IsIQueryableType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        return SymbolEqualityComparer.Default.Equals(type, _queryableType) ||
               TryGetConstructedInterface(type, _queryableGenericType, out _) ||
               type.AllInterfaces.Any(i =>
                   SymbolEqualityComparer.Default.Equals(i, _queryableType));
    }

    private bool IsIEnumerableLike(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(type, _enumerableType))
            return true;

        return TryGetConstructedInterface(type, _enumerableGenericType, out _) ||
               type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _enumerableType));
    }

    private bool TryGetConstructedInterface(ITypeSymbol? type, INamedTypeSymbol? interfaceType, out INamedTypeSymbol match)
    {
        match = null!;
        if (type == null || interfaceType == null)
            return false;

        if (type is INamedTypeSymbol namedType &&
            SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, interfaceType))
        {
            match = namedType;
            return true;
        }

        foreach (var currentInterface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(currentInterface.OriginalDefinition, interfaceType))
            {
                match = currentInterface;
                return true;
            }
        }

        return false;
    }

    private bool TryGetExecutableRoot(IMethodSymbol method, out IOperation executableRoot)
    {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            var semanticModel = _compilation.GetSemanticModel(syntax.SyntaxTree);

            switch (syntax)
            {
                case MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null:
                    var methodBody = semanticModel.GetOperation(methodDeclaration.Body);
                    if (methodBody != null)
                    {
                        executableRoot = methodBody;
                        return true;
                    }

                    break;

                case MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null:
                    var methodExpressionBody = semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression);
                    if (methodExpressionBody != null)
                    {
                        executableRoot = methodExpressionBody;
                        return true;
                    }

                    break;

                case LocalFunctionStatementSyntax localFunction when localFunction.Body != null:
                    var localFunctionBody = semanticModel.GetOperation(localFunction.Body);
                    if (localFunctionBody != null)
                    {
                        executableRoot = localFunctionBody;
                        return true;
                    }

                    break;

                case LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null:
                    var localFunctionExpressionBody = semanticModel.GetOperation(localFunction.ExpressionBody.Expression);
                    if (localFunctionExpressionBody != null)
                    {
                        executableRoot = localFunctionExpressionBody;
                        return true;
                    }

                    break;
            }
        }

        executableRoot = null!;
        return false;
    }

    private IEnumerable<IOperation> EnumerateOperations(IOperation executableRoot)
    {
        yield return executableRoot;

        foreach (var operation in executableRoot.Descendants())
        {
            if (IsInsideNestedExecutable(operation, executableRoot))
                continue;

            yield return operation;
        }
    }

    private static bool IsInsideNestedExecutable(IOperation operation, IOperation executableRoot)
    {
        var current = operation.Parent;
        while (current != null && !ReferenceEquals(current, executableRoot))
        {
            if (current is ILocalFunctionOperation or IAnonymousFunctionOperation)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static IMethodSymbol GetOriginalTargetMethod(IMethodSymbol method)
    {
        return method.ReducedFrom ?? method;
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
