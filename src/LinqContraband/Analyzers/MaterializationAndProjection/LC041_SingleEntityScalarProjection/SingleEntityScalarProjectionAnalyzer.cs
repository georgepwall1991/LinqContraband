using System;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class SingleEntityScalarProjectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC041";
    internal const string PropertyNameKey = "PropertyName";

    private const string Category = "Performance";
    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "First",
        "FirstOrDefault",
        "FirstAsync",
        "FirstOrDefaultAsync",
        "Single",
        "SingleOrDefault",
        "SingleAsync",
        "SingleOrDefaultAsync");

    private static readonly ImmutableHashSet<string> QuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "Include",
        "ThenInclude",
        "IgnoreQueryFilters",
        "AsSplitQuery",
        "AsSingleQuery",
        "AsTracking",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "IgnoreAutoIncludes",
        "TagWith",
        "TagWithCallSite");

    private static readonly LocalizableString Title = "Single entity query over-fetches one consumed property";

    private static readonly LocalizableString MessageFormat =
        "Query materializes '{0}' but only property '{1}' is consumed in the same scope. Consider projecting that property before materializing.";

    private static readonly LocalizableString Description =
        "When a query result is only used for one scalar property, projecting that property can avoid materializing the full entity.";

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

        if (!TargetMethods.Contains(method.Name))
            return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return;

        if (!TryGetEntityType(receiver, out var entityType))
            return;

        if (HasSelectInChain(receiver))
            return;

        if (TryGetPredicateLambda(invocation, out var lambda) &&
            IsPrimaryKeyLookup(lambda, entityType))
        {
            return;
        }

        if (!TryGetAssignedLocal(invocation, out var assignedLocal))
            return;

        var executableRoot = invocation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return;

        if (!TryAnalyzeLocalUsage(executableRoot, assignedLocal, out var property))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(PropertyNameKey, property.Name),
                method.Name,
                property.Name));
    }
}
