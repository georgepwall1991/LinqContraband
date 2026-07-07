using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

public sealed partial class PrematureMaterializationAnalyzer
{
    public const string DiagnosticId = "LC002";
    private const string Category = "Performance";
    private const string HelpLinkUri =
        "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC002_PrematureMaterialization.md";

    internal const string DiagnosticKindKey = "DiagnosticKind";
    internal const string OriginKindKey = "OriginKind";
    internal const string CurrentMethodKey = "CurrentMethod";
    internal const string MaterializerKey = "Materializer";
    internal const string FixKindKey = "FixKind";

    internal const string ContinuationDiagnosticKind = "Continuation";
    internal const string RedundantDiagnosticKind = "Redundant";

    internal const string InlineInvocationOriginKind = "InlineInvocation";
    internal const string LocalOriginKind = "Local";
    internal const string ConstructorOriginKind = "Constructor";

    internal const string MoveBeforeMaterializationFixKind = "MoveBeforeMaterialization";
    internal const string RemoveRedundantMaterializationFixKind = "RemoveRedundantMaterialization";

    private static readonly LocalizableString Title = "Premature query continuation after materialization";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' after materializing an IQueryable forces the operation to run in memory";

    private static readonly LocalizableString RedundantMessageFormat =
        "The call to '{0}' is redundant because the sequence was already materialized by '{1}'";

    private static readonly LocalizableString Description =
        "Keep approved query operations on IQueryable before materialization and avoid redundant second materializers.";

    private static readonly ImmutableHashSet<string> SequenceContinuationMethods = ImmutableHashSet.Create(
        "Where",
        "Select",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take"
    );

    private static readonly ImmutableHashSet<string> TerminalContinuationMethods = ImmutableHashSet.Create(
        "Count",
        "LongCount",
        "Any",
        "All",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault",
        "Min",
        "Max",
        "Sum",
        "Average"
    );

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

    public static readonly DiagnosticDescriptor RedundantRule = new(
        DiagnosticId,
        "Redundant materialization",
        RedundantMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpLinkUri);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, RedundantRule);

    private static ImmutableDictionary<string, string?> CreateProperties(
        string diagnosticKind,
        string originKind,
        string currentMethod,
        string materializer)
    {
        return ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticKindKey, diagnosticKind)
            .Add(OriginKindKey, originKind)
            .Add(CurrentMethodKey, currentMethod)
            .Add(MaterializerKey, materializer);
    }
}
