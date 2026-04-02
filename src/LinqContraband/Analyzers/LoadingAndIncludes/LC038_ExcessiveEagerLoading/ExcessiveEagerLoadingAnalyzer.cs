using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC038_ExcessiveEagerLoading;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ExcessiveEagerLoadingAnalyzer : DiagnosticAnalyzer
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
}
