using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class NestedSaveChangesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC039";
    private const string Category = "Reliability";

    private static readonly LocalizableString Title = "Avoid repeated SaveChanges on the same context";

    private static readonly LocalizableString MessageFormat =
        "Context '{0}' calls '{1}' multiple times in the same method. Consider batching changes into a single save.";

    private static readonly LocalizableString Description =
        "Reports when the same provable DbContext is saved more than once inside one executable root, unless a transaction boundary separates the calls.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        Description,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    private static readonly ImmutableHashSet<string> SaveMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "SaveChanges",
        "SaveChangesAsync");

    private static readonly ImmutableHashSet<string> TransactionBoundaryMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "BeginTransaction",
        "BeginTransactionAsync",
        "Commit",
        "CommitAsync",
        "Rollback",
        "RollbackAsync",
        "CreateSavepoint",
        "CreateSavepointAsync",
        "ReleaseSavepoint",
        "ReleaseSavepointAsync",
        "RollbackToSavepoint",
        "RollbackToSavepointAsync");

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
        private readonly ConcurrentBag<InvocationRecord> _records = new();

        public void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (!SaveMethodNames.Contains(invocation.TargetMethod.Name) &&
                !TransactionBoundaryMethodNames.Contains(invocation.TargetMethod.Name))
            {
                return;
            }

            var root = invocation.FindOwningExecutableRoot();
            if (root == null)
                return;

            if (invocation.TargetMethod.Name is not ("SaveChanges" or "SaveChangesAsync"))
            {
                _records.Add(new InvocationRecord(root, invocation.Syntax.GetLocation(), invocation.Syntax.SpanStart, null, true, invocation.TargetMethod.Name));
                return;
            }

            if (!invocation.TargetMethod.ContainingType.IsDbContext())
                return;

            if (!TryGetContextSymbol(invocation.Instance?.UnwrapConversions(), out var contextSymbol))
                return;

            _records.Add(new InvocationRecord(root, invocation.Syntax.GetLocation(), invocation.Syntax.SpanStart, contextSymbol, false, invocation.TargetMethod.Name));
        }

        public void ReportDiagnostics(CompilationAnalysisContext context)
        {
            var groupedByRoot = _records
                .GroupBy(record => record.Root, OperationRootComparer.Instance)
                .ToArray();

            foreach (var rootGroup in groupedByRoot)
            {
                var boundaries = rootGroup
                    .Where(record => record.IsBoundary)
                    .Select(record => record.Position)
                    .OrderBy(position => position)
                    .ToArray();

                var savesByContext = rootGroup
                    .Where(record => !record.IsBoundary && record.ContextSymbol != null)
                    .GroupBy(record => record.ContextSymbol!, SymbolEqualityComparer.Default);

                foreach (var contextGroup in savesByContext)
                {
                    var saves = contextGroup
                        .OrderBy(record => record.Position)
                        .ToArray();

                    if (saves.Length < 2)
                        continue;

                    for (var i = 1; i < saves.Length; i++)
                    {
                        var previous = saves[i - 1];
                        var current = saves[i];

                        if (HasTransactionBoundaryBetween(boundaries, previous.Position, current.Position))
                            continue;

                        Report(context, current, contextGroup.Key, current.MethodName);
                    }
                }
            }
        }

    }

    private sealed class InvocationRecord
    {
        public InvocationRecord(IOperation root, Location location, int position, ISymbol? contextSymbol, bool isBoundary, string methodName)
        {
            Root = root;
            Location = location;
            Position = position;
            ContextSymbol = contextSymbol;
            IsBoundary = isBoundary;
            MethodName = methodName;
        }

        public IOperation Root { get; }
        public Location Location { get; }
        public int Position { get; }
        public ISymbol? ContextSymbol { get; }
        public bool IsBoundary { get; }
        public string MethodName { get; }
    }

    private sealed class OperationRootComparer : IEqualityComparer<IOperation>
    {
        public static readonly OperationRootComparer Instance = new();

        public bool Equals(IOperation? x, IOperation? y) => ReferenceEquals(x, y);
        public int GetHashCode(IOperation obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
