using System.Collections.Concurrent;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class UseFrozenSetForStaticMembershipCachesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC033";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Use FrozenSet for provably read-only membership caches";

    private static readonly LocalizableString MessageFormat =
        "Field '{0}' is a provably read-only membership cache. Consider FrozenSet<T> for faster steady-state Contains lookups on .NET 8+.";

    private static readonly LocalizableString Description =
        "Reports only when a private static readonly HashSet<T> has a fixer-safe initializer and every source reference is a direct Contains call outside IQueryable or expression-tree contexts.";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC033_UseFrozenSetForStaticMembershipCaches.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        if (!UseFrozenSetForStaticMembershipCachesAnalysis.TryGetFrozenSetSupport(context.Compilation, out var support))
            return;

        var state = new AnalysisState(context.Compilation, support);
        context.RegisterSymbolAction(state.AnalyzeField, SymbolKind.Field);
        context.RegisterOperationAction(state.AnalyzeFieldReference, OperationKind.FieldReference);
        context.RegisterCompilationEndAction(state.ReportDiagnostics);
    }

    private sealed partial class AnalysisState
    {
        private readonly Compilation _compilation;
        private readonly FrozenSetSupport _support;
        private readonly ConcurrentDictionary<IFieldSymbol, CandidateField> _candidates =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IFieldSymbol, int> _allowedUsageCounts =
            new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<IFieldSymbol, byte> _disallowedUsages =
            new(SymbolEqualityComparer.Default);

        public AnalysisState(Compilation compilation, FrozenSetSupport support)
        {
            _compilation = compilation;
            _support = support;
        }

        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var properties = ImmutableDictionary<string, string?>.Empty.Add(
                UseFrozenSetForStaticMembershipCachesDiagnosticProperties.FixerEligible,
                "true");

            foreach (var pair in _candidates)
            {
                var field = pair.Key;
                var candidate = pair.Value;

                if (_disallowedUsages.ContainsKey(field))
                    continue;

                if (!_allowedUsageCounts.TryGetValue(field, out var allowedCount) || allowedCount == 0)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(Rule, candidate.Location, properties, field.Name));
            }
        }
    }

    private sealed class CandidateField
    {
        public CandidateField(IFieldSymbol field, Location location)
        {
            Field = field;
            Location = location;
        }

        public IFieldSymbol Field { get; }
        public Location Location { get; }
    }
}
