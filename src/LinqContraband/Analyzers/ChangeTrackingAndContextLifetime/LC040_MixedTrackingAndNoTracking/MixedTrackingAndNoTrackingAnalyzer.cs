using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC040_MixedTrackingAndNoTracking;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MixedTrackingAndNoTrackingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC040";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title = "Avoid mixing tracking modes on the same context";

    private static readonly LocalizableString MessageFormat =
        "Context '{0}' uses both tracked and no-tracking materialization in the same method. Pick one mode for this scope.";

    private static readonly LocalizableString Description =
        "Reports when the same provable DbContext materializes entities with and without tracking inside one executable root.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        Description,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    private static readonly ImmutableHashSet<string> MaterializerNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
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
        "ToDictionary",
        "ToDictionaryAsync",
        "ToHashSet",
        "ToHashSetAsync",
        "AsEnumerable");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        var state = new AnalysisState();
        context.RegisterOperationAction(state.AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterCompilationEndAction(state.ReportDiagnostics);
    }

    private sealed partial class AnalysisState
    {
        private readonly ConcurrentBag<MaterializationRecord> _records = new();

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (!MaterializerNames.Contains(invocation.TargetMethod.Name))
                return;

            if (!invocation.TargetMethod.Name.IsMaterializerMethod())
                return;

            var root = invocation.FindOwningExecutableRoot();
            if (root == null)
                return;

            if (!TryGetTrackingMode(invocation, root, out var trackingMode))
                return;

            if (!TryGetContextSymbol(invocation, root, out var contextSymbol))
                return;

            _records.Add(new MaterializationRecord(root, invocation.Syntax.GetLocation(), invocation.Syntax.SpanStart, contextSymbol, trackingMode));
        }

        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var groupedByRoot = _records
                .GroupBy(record => record.Root, OperationRootComparer.Instance)
                .ToArray();

            foreach (var rootGroup in groupedByRoot)
            {
                foreach (var contextGroup in rootGroup
                             .Where(record => record.ContextSymbol != null)
                             .GroupBy(record => record.ContextSymbol!, SymbolEqualityComparer.Default))
                {
                    var records = contextGroup
                        .OrderBy(record => record.Position)
                        .ToArray();

                    if (records.Length < 2)
                        continue;

                    var firstMode = records[0].Mode;
                    var reported = false;

                    for (var i = 1; i < records.Length; i++)
                    {
                        var current = records[i];
                        if (current.Mode == firstMode || reported)
                            continue;

                        context.ReportDiagnostic(
                            Diagnostic.Create(Rule, current.Location, contextGroup.Key.Name));
                        reported = true;
                    }
                }
            }
        }
    }

    private sealed class MaterializationRecord
    {
        public MaterializationRecord(IOperation root, Location location, int position, ISymbol? contextSymbol, TrackingMode mode)
        {
            Root = root;
            Location = location;
            Position = position;
            ContextSymbol = contextSymbol;
            Mode = mode;
        }

        public IOperation Root { get; }
        public Location Location { get; }
        public int Position { get; }
        public ISymbol? ContextSymbol { get; }
        public TrackingMode Mode { get; }
    }

    private enum TrackingMode
    {
        Tracked,
        NoTracking
    }

    private sealed class OperationRootComparer : IEqualityComparer<IOperation>
    {
        public static readonly OperationRootComparer Instance = new();

        public bool Equals(IOperation? x, IOperation? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOperation obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
