using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Analyzes Entity Framework Core queries to detect missing AsNoTracking() calls in read-only operations. Diagnostic ID: LC009
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> When querying entities for read-only operations, EF Core creates change tracking snapshots
/// by default, which consumes memory and CPU time. Using AsNoTracking() prevents unnecessary tracking overhead and improves
/// performance in scenarios where entities are not being modified.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC009";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Performance: Missing AsNoTracking() in Read-Only path";

    private static readonly LocalizableString MessageFormat =
        "Method '{0}' appears to be read-only but returns tracked entities. Use AsNoTracking() to avoid tracking overhead.";

    private static readonly LocalizableString Description =
        "When querying entities for read-only operations, use .AsNoTracking() to prevent EF Core from creating unnecessary change tracking snapshots. This reduces memory usage and CPU time.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC009_MissingAsNoTracking.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var writeOperationCache = new ConcurrentDictionary<SyntaxNode, bool>();
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, writeOperationCache),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        ConcurrentDictionary<SyntaxNode, bool> writeOperationCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsEntityMaterializer(method))
            return;

        var enclosingSymbol = context.Operation.SemanticModel?.GetEnclosingSymbol(invocation.Syntax.SpanStart);
        if (enclosingSymbol is IMethodSymbol enclosingMethod && enclosingMethod.ReturnType.IsIQueryable())
            return;

        if (!AnalyzeQueryChain(invocation).IsTrackedEfRead)
            return;

        // db.Orders.AsEnumerable().Where(...).ToList(): AsEnumerable() defers, so the query runs
        // at the outer ToList(), which reports it. AsEnumerable() reports only when nothing does.
        if (method.Name == "AsEnumerable" && QueryRunsAtReportedMaterializer(invocation))
            return;

        if (HasWriteOperations(context.Operation, writeOperationCache))
            return;

        // db.Orders.ToList().Where(...).ToList(): the entities live on in the outer, in-memory
        // materializer, so that is where they are followed.
        var resultAnchor = FindInMemoryResultAnchor(invocation);

        var root = invocation.FindOwningExecutableRoot();
        var entityLocals = root == null
            ? new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
            : CollectEntityLocals(resultAnchor, root, context.CancellationToken);

        // A mutation of the materialized entity marks this as a write path even when the
        // SaveChanges lives in a helper the analyzer cannot see — suggesting AsNoTracking
        // would break that cross-method save.
        if (root != null && MaterializedEntityIsMutated(resultAnchor, root, entityLocals, context.CancellationToken))
            return;

        // Entities that leave the method may be changed and saved by code this analysis does
        // not see, so the rule reports without offering the one-click fix.
        var properties = root == null || MaterializedEntitiesEscape(resultAnchor, root, entityLocals, context.CancellationToken)
            ? EscapeProperties
            : ImmutableDictionary<string, string?>.Empty;

        var containingMethodName = GetContainingMethodName(context.Operation);
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, containingMethodName));
    }

    private static readonly ImmutableDictionary<string, string?> EscapeProperties =
        ImmutableDictionary<string, string?>.Empty.Add(EntitiesEscapeProperty, "true");

    // Names the member the query sits in. A lambda has no name of its own (its symbol's Name
    // is empty, which printed "Method ''"), so step out to the method, local function or
    // accessor that contains it.
    private static string GetContainingMethodName(IOperation operation)
    {
        var sym = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        while (sym is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction })
            sym = sym.ContainingSymbol;

        if (sym is IMethodSymbol method)
        {
            if (method.AssociatedSymbol != null)
                sym = method.AssociatedSymbol;
            else if (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
                sym = method.ContainingType;
            else if (method.Name == TopLevelStatementsEntryPointName)
                return "<top-level statements>";
        }

        var name = sym?.Name;
        return string.IsNullOrEmpty(name) ? "Unknown" : name!;
    }

    // The compiler-generated entry point that holds top-level statements.
    private const string TopLevelStatementsEntryPointName = "<Main>$";

    private sealed class ChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool IsAmbiguousSource { get; set; }
        public bool HasAsNoTracking { get; set; }
        public bool HasAsTracking { get; set; }
        public bool HasSelect { get; set; }

        public bool IsTrackedEfRead =>
            IsEfQuery && !IsAmbiguousSource && !HasAsNoTracking && !HasAsTracking && !HasSelect;
    }
}
