using System;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Analyzes database query execution inside loops, causing N+1 query problems. Diagnostic ID: LC007
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Executing database queries inside a loop creates a separate database roundtrip for every
/// loop iteration, resulting in N+1 total queries (1 query to get the collection + N queries inside the loop). Each database
/// roundtrip adds network latency (typically 1-50ms), which multiplies catastrophically with large collections. For example,
/// a loop over 1000 items with 10ms latency per query adds 10 seconds of pure waiting time. Always fetch required data in
/// bulk outside the loop using techniques like Include(), joins, or dictionary lookups.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NPlusOneLooperAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC007";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "N+1 Problem: Database query inside loop";

    private static readonly LocalizableString MessageFormat =
        "Executing '{0}' inside a loop causes N+1 queries. Fetch data in bulk outside the loop.";

    private static readonly LocalizableString Description =
        "Performing database queries inside a loop results in a database roundtrip for every iteration. This destroys performance via network latency.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // 1. Check if this is a DB execution method
        if (!IsDbExecutionMethod(method, invocation)) return;

        // 2. Check if inside a loop
        if (IsInsideLoop(invocation))
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
    }

    private static bool IsDbExecutionMethod(IMethodSymbol method, IInvocationOperation invocation)
    {
        // Case 1: DbSet.Find / FindAsync
        if (method.Name.StartsWith("Find", StringComparison.Ordinal) && method.ContainingType.IsDbSet()) return true;

        // Case 2: Explicit loading entry points (might trigger lazy loading later or be used with .Load())
        if (method.Name is "Reference" or "Collection")
        {
            var ns = method.ContainingType?.ContainingNamespace?.ToString();
            if (ns == "Microsoft.EntityFrameworkCore" || ns == "Microsoft.EntityFrameworkCore.ChangeTracking")
                return true;
        }

        // Case 3: IQueryable materializers (ToList, Count, First, etc.)
        if (!method.Name.IsMaterializerMethod()) return false;

        var receiverType = invocation.GetInvocationReceiverType();

        return receiverType?.IsIQueryable() == true;
    }

    private bool IsInsideLoop(IOperation operation)
    {
        return operation.IsInsideLoop() || operation.IsInsideAsyncForEach();
    }
}
