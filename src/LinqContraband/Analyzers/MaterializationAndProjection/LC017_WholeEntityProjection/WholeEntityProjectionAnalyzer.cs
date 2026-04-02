using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Analyzes Entity Framework Core queries to detect loading entire entities when only a few properties are accessed.
/// Diagnostic ID: LC017
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Loading entire entities when only 1-2 properties are needed wastes bandwidth,
/// memory, and CPU. For large entities with 10+ properties, using .Select() projection can dramatically reduce
/// data transfer and improve performance.</para>
/// <para><b>Conservative detection:</b> This analyzer only reports when clearly wasteful patterns are detected:
/// entities with 10+ properties where only 1-2 are accessed within local scope.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class WholeEntityProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC017";
    private const string Category = "Performance";
    private const int MinPropertyThreshold = 10;
    private const int MaxAccessedProperties = 2;

    private static readonly LocalizableString Title = "Performance: Consider using Select() projection";

    private static readonly LocalizableString MessageFormat =
        "Query loads entire '{0}' entity but only {1} of {2} properties are accessed. Consider using .Select() projection.";

    private static readonly LocalizableString Description =
        "Loading entire entities when only a few properties are needed wastes bandwidth and memory. " +
        "Use .Select() to project only the required fields for improved performance.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
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

        if (!IsCollectionMaterializer(method)) return;

        var analysis = AnalyzeQueryChain(invocation);
        if (!analysis.IsEfQuery) return;
        if (analysis.HasSelect) return;
        if (analysis.EntityType == null) return;

        var properties = GetEntityProperties(analysis.EntityType);
        if (properties.Count < MinPropertyThreshold) return;

        var variableInfo = FindVariableAssignment(invocation);
        if (variableInfo == null) return;

        var usage = AnalyzeVariableUsage(invocation, variableInfo.Value.Symbol, analysis.EntityType);
        if (usage.HasEscapingUsage) return;
        if (usage.AccessedProperties.Count > MaxAccessedProperties) return;
        if (usage.AccessedProperties.Count == 0) return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                analysis.EntityType.Name,
                usage.AccessedProperties.Count,
                properties.Count));
    }

    private static bool IsCollectionMaterializer(IMethodSymbol method)
    {
        return method.Name is "ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync";
    }

    private sealed class VariableUsageAnalysis
    {
        public HashSet<string> AccessedProperties { get; } = new();
        public bool HasEscapingUsage { get; set; }
    }

    private class QueryChainAnalysis
    {
        public bool IsEfQuery { get; set; }
        public bool HasSelect { get; set; }
        public ITypeSymbol? EntityType { get; set; }
    }
}
