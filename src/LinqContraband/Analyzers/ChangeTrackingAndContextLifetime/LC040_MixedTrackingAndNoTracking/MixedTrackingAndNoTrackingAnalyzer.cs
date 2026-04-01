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
public sealed class MixedTrackingAndNoTrackingAnalyzer : DiagnosticAnalyzer
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

    private sealed class AnalysisState
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

        private static bool TryGetTrackingMode(IInvocationOperation invocation, IOperation root, out TrackingMode mode)
        {
            mode = TrackingMode.Tracked;

            IOperation? current = invocation.GetInvocationReceiver();
            while (current != null)
            {
                current = current.UnwrapConversions();

                switch (current)
                {
                    case IInvocationOperation nestedInvocation:
                        if (nestedInvocation.TargetMethod.Name is "AsTracking")
                        {
                            mode = TrackingMode.Tracked;
                            return true;
                        }

                        if (nestedInvocation.TargetMethod.Name is "AsNoTracking" or "AsNoTrackingWithIdentityResolution")
                        {
                            mode = TrackingMode.NoTracking;
                            return true;
                        }

                        if (nestedInvocation.TargetMethod.Name == "Select")
                            return false;

                        current = nestedInvocation.GetInvocationReceiver();
                        continue;

                    case ILocalReferenceOperation localReference:
                        if (!TryResolveAssignedValue(localReference.Local, root, out var assignedValue))
                            return false;

                        current = assignedValue;
                        continue;

                    case IPropertyReferenceOperation propertyReference:
                    case IFieldReferenceOperation fieldReference:
                        return true;

                    case IParameterReferenceOperation:
                        return false;

                    default:
                        return true;
                }
            }

            return true;
        }

        private static bool TryGetContextSymbol(IInvocationOperation invocation, IOperation root, out ISymbol? contextSymbol)
        {
            contextSymbol = null;

            var current = invocation.GetInvocationReceiver();
            while (current != null)
            {
                current = current.UnwrapConversions();

                if (current is IInvocationOperation nestedInvocation)
                {
                    if (nestedInvocation.TargetMethod.Name == "Select")
                        return false;

                    if (nestedInvocation.TargetMethod.Name == "Set" &&
                        nestedInvocation.TargetMethod.ContainingType.IsDbContext())
                        return TryGetSymbol(nestedInvocation.Instance, out contextSymbol);

                    current = nestedInvocation.GetInvocationReceiver();
                    continue;
                }

                switch (current)
                {
                    case IPropertyReferenceOperation propertyReference when propertyReference.Type.IsDbSet():
                        return TryGetSymbol(propertyReference.Instance, out contextSymbol);

                    case IFieldReferenceOperation fieldReference when fieldReference.Type.IsDbSet():
                        return TryGetSymbol(fieldReference.Instance, out contextSymbol);

                    case ILocalReferenceOperation localReference:
                        if (!TryResolveAssignedValue(localReference.Local, root, out var assignedValue))
                            return false;

                        current = assignedValue;
                        continue;

                    case IParameterReferenceOperation:
                        return false;

                    default:
                        return false;
                }
            }

            return false;
        }

        private static bool TryGetSymbol(IOperation? operation, out ISymbol? symbol)
        {
            switch (operation?.UnwrapConversions())
            {
                case ILocalReferenceOperation localReference:
                    symbol = localReference.Local;
                    return true;
                case IParameterReferenceOperation parameterReference:
                    symbol = parameterReference.Parameter;
                    return true;
                case IFieldReferenceOperation fieldReference:
                    symbol = fieldReference.Field;
                    return true;
                case IPropertyReferenceOperation propertyReference:
                    symbol = propertyReference.Property;
                    return true;
                default:
                    symbol = null;
                    return false;
            }
        }

        private static bool TryResolveAssignedValue(ILocalSymbol local, IOperation root, out IOperation? assignedValue)
        {
            assignedValue = null;
            var matches = 0;

            foreach (var descendant in root.Descendants())
            {
                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                    SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
                {
                    matches++;
                    assignedValue = assignment.Value;
                }
                else if (descendant is IVariableDeclaratorOperation declarator &&
                         SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                         declarator.Initializer != null)
                {
                    matches++;
                    assignedValue = declarator.Initializer.Value;
                }

                if (matches > 1)
                    return false;
            }

            return matches == 1 && assignedValue != null;
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
