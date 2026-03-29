using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingQueryTagsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC042";
    private const string Category = "Performance";
    private const int DefaultThreshold = 3;
    private const string ThresholdKey = "dotnet_code_quality.LC042.query_operator_threshold";

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Any",
        "AnyAsync",
        "All",
        "AllAsync",
        "Count",
        "CountAsync",
        "LongCount",
        "LongCountAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "Last",
        "LastAsync",
        "LastOrDefault",
        "LastOrDefaultAsync",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync");

    private static readonly ImmutableHashSet<string> QuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "Select",
        "SelectMany",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "GroupBy",
        "Join",
        "Include",
        "ThenInclude",
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsTracking",
        "AsSplitQuery",
        "AsSingleQuery",
        "IgnoreQueryFilters",
        "IgnoreAutoIncludes",
        "OfType",
        "TagWith",
        "TagWithCallSite");

    private static readonly LocalizableString Title = "Complex query should be tagged";

    private static readonly LocalizableString MessageFormat =
        "Query uses {0} tracked steps but has no TagWith/TagWithCallSite. Consider tagging complex queries for diagnostics and observability.";

    private static readonly LocalizableString Description =
        "Complex EF Core queries are easier to trace when they are tagged. This advisory reports only when the query shape is clearly non-trivial and untagged.";

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
        if (!TargetMethods.Contains(invocation.TargetMethod.Name))
            return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return;

        if (!TryAnalyzeChain(receiver, out var count))
            return;

        var threshold = GetThreshold(context.Options.AnalyzerConfigOptionsProvider, invocation.Syntax.SyntaxTree);
        if (count < threshold)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), count));
    }

    private static bool TryAnalyzeChain(IOperation receiver, out int count)
    {
        count = 0;
        var current = receiver;

        while (current != null)
        {
            current = current.UnwrapConversions();

            if (current is IInvocationOperation invocation)
            {
                var methodName = invocation.TargetMethod.Name;

                if (methodName is "TagWith" or "TagWithCallSite")
                    return false;

                if (QuerySteps.Contains(methodName))
                {
                    count++;
                    current = invocation.GetInvocationReceiver();
                    continue;
                }

                if (methodName == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                {
                    return true;
                }

                return false;
            }

            if (current.Type != null && current.Type.IsDbSet())
                return true;

            if (current is IPropertyReferenceOperation propertyReference &&
                propertyReference.Type.IsDbSet())
            {
                return true;
            }

            if (current is IFieldReferenceOperation fieldReference &&
                fieldReference.Type.IsDbSet())
            {
                return true;
            }

            if (current is ILocalReferenceOperation localReference &&
                localReference.Type.IsDbSet())
            {
                return true;
            }

            if (current is IParameterReferenceOperation parameterReference &&
                parameterReference.Type.IsDbSet())
            {
                return true;
            }

            if (current is IPropertyReferenceOperation or IFieldReferenceOperation or ILocalReferenceOperation or IParameterReferenceOperation)
                return false;

            return false;
        }

        return false;
    }

    private static int GetThreshold(AnalyzerConfigOptionsProvider provider, SyntaxTree syntaxTree)
    {
        var options = provider.GetOptions(syntaxTree);
        if (options.TryGetValue(ThresholdKey, out var value) && int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;

        return DefaultThreshold;
    }
}
