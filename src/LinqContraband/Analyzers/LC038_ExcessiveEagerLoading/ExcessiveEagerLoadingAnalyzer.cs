using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC038_ExcessiveEagerLoading;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExcessiveEagerLoadingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC038";
    internal const string ThresholdOptionKey = "dotnet_code_quality.LC038.include_threshold";

    private const string Category = "Performance";
    private const int DefaultThreshold = 4;

    private static readonly LocalizableString Title = "Avoid excessive eager loading";

    private static readonly LocalizableString MessageFormat =
        "Query uses {0} Include/ThenInclude calls (threshold: {1}). Consider projecting less data or splitting the query.";

    private static readonly LocalizableString Description =
        "Reports only when a provable EF query chain uses a high number of Include/ThenInclude calls on the same query root.";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        Description);

    private static readonly ImmutableHashSet<string> IncludeLikeMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Include",
        "ThenInclude");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var thresholdCache = new ConditionalWeakTable<SyntaxTree, StrongBox<int>>();
        context.RegisterOperationAction(operationContext => AnalyzeInvocation(operationContext, thresholdCache), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ConditionalWeakTable<SyntaxTree, StrongBox<int>> thresholdCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsIncludeLike(invocation.TargetMethod))
            return;

        if (HasIncludeAncestor(invocation))
            return;

        if (!TryCountIncludeChain(invocation, out var includeCount))
            return;

        var threshold = GetThreshold(context, thresholdCache);
        if (includeCount < threshold)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), includeCount, threshold));
    }

    private static bool TryCountIncludeChain(IInvocationOperation outermostInvocation, out int includeCount)
    {
        includeCount = 0;

        IOperation? current = outermostInvocation;
        while (current is IInvocationOperation invocation && IsIncludeLike(invocation.TargetMethod))
        {
            includeCount++;
            current = invocation.GetInvocationReceiver();
        }

        if (current == null)
            return false;

        return IsProvableEfRoot(current);
    }

    private static bool IsProvableEfRoot(IOperation operation)
    {
        operation = operation.UnwrapConversions();

        return operation switch
        {
            IPropertyReferenceOperation propertyReference => propertyReference.Type.IsDbSet(),
            IFieldReferenceOperation fieldReference => fieldReference.Type.IsDbSet(),
            IInvocationOperation invocation => IsDbContextSetInvocation(invocation),
            _ => false
        };
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext();
    }

    private static bool IsIncludeLike(IMethodSymbol method)
    {
        return IncludeLikeMethods.Contains(method.Name) &&
               method.ContainingNamespace?.ToString()?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
    }

    private static bool HasIncludeAncestor(IInvocationOperation invocation)
    {
        for (var current = invocation.Parent; current != null; current = current.Parent)
        {
            if (current is not IInvocationOperation parentInvocation)
                continue;

            if (!IsIncludeLike(parentInvocation.TargetMethod))
                continue;

            if (InvocationUsesReceiverChain(parentInvocation.GetInvocationReceiver(), invocation))
                return true;
        }

        return false;
    }

    private static bool InvocationUsesReceiverChain(IOperation? current, IInvocationOperation target)
    {
        current = current?.UnwrapConversions();

        while (current != null)
        {
            if (ReferenceEquals(current, target))
                return true;

            if (current is IInvocationOperation invocation)
            {
                current = invocation.GetInvocationReceiver();
                continue;
            }

            break;
        }

        return false;
    }

    private static int GetThreshold(OperationAnalysisContext context, ConditionalWeakTable<SyntaxTree, StrongBox<int>> thresholdCache)
    {
        var syntaxTree = context.Operation.Syntax.SyntaxTree;
        if (thresholdCache.TryGetValue(syntaxTree, out var cached))
            return cached.Value;

        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
        var threshold = DefaultThreshold;

        if (options.TryGetValue(ThresholdOptionKey, out var value) &&
            int.TryParse(value, out var configuredThreshold) &&
            configuredThreshold > 0)
        {
            threshold = configuredThreshold;
        }

        thresholdCache.Add(syntaxTree, new StrongBox<int>(threshold));
        return threshold;
    }
}
