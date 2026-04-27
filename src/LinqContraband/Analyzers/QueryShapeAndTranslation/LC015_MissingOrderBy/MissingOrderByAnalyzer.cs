using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Analyzes IQueryable operations (Skip, Last, Chunk) that require ordering but are called on unordered sequences. Diagnostic ID: LC015
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Operations like Skip, Last, and Chunk depend on a specific ordering to produce deterministic
/// results. Without an explicit OrderBy or OrderByDescending, the database may return results in any order, leading to
/// non-deterministic behavior in pagination, retrieval of last elements, or chunking operations. This can cause unpredictable
/// application behavior and difficult-to-reproduce bugs.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MissingOrderByAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC015";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Deterministic Pagination: OrderBy required before Skip/Take";

    private static readonly LocalizableString MessageFormat =
        "The method '{0}' is called on an unordered IQueryable. Call 'OrderBy' or 'OrderByDescending' first to ensure deterministic results.";

    private static readonly LocalizableString MisplacedMessageFormat =
        "The method '{0}' is called after 'Skip' or 'Take'. This results in sorting a subset of the data rather than the full set.";

    private static readonly LocalizableString Description =
        "Pagination and Last operations on unordered IQueryables are non-deterministic. Sorting must happen before Skip/Take.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    public static readonly DiagnosticDescriptor MisplacedRule = new(
        DiagnosticId, "OrderBy after Skip/Take", MisplacedMessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    private static readonly ImmutableHashSet<string> PaginationMethods = ImmutableHashSet.Create(
        "Skip", "Take", "Last", "LastOrDefault", "Chunk"
    );

    private static readonly ImmutableHashSet<string> SortingMethods = ImmutableHashSet.Create(
        "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, MisplacedRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockStartAction(InitializeOperationBlock);
    }

    private void InitializeOperationBlock(OperationBlockStartAnalysisContext context)
    {
        var localValueCache = new LocalValueCache();
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, localValueCache),
            OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context, LocalValueCache localValueCache)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        var isSorting = SortingMethods.Contains(method.Name);
        var isPagination = PaginationMethods.Contains(method.Name);
        if (!isSorting && !isPagination)
            return;

        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null || !receiver.Type.IsIQueryable())
            return;

        if (!HasEntityFrameworkQuerySource(receiver, localValueCache, context.CancellationToken))
            return;

        if (isSorting)
        {
            if (HasPaginationUpstream(receiver))
                context.ReportDiagnostic(Diagnostic.Create(MisplacedRule, GetMethodLocation(invocation), method.Name));
            return;
        }

        if (!HasOrderByUpstream(receiver) &&
            !HasPaginationUpstream(receiver) &&
            !HasSortingDownstream(invocation))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, GetMethodLocation(invocation), method.Name));
        }
    }

    private static bool HasEntityFrameworkQuerySource(
        IOperation operation,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken)
    {
        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var current = operation;
        while (current != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = current.UnwrapConversions();

            if (current.Type.IsDbSet())
                return true;

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (invocation.TargetMethod.Name == "Set" && invocation.TargetMethod.ContainingType.IsDbContext())
                        return true;

                    current = invocation.GetInvocationReceiver();
                    continue;

                case IPropertyReferenceOperation propertyReference:
                    current = propertyReference.Instance;
                    continue;

                case IFieldReferenceOperation fieldReference:
                    current = fieldReference.Instance;
                    continue;

                case ILocalReferenceOperation localReference:
                    if (!visitedLocals.Add(localReference.Local))
                        return false;

                    if (localReference.Type.IsDbSet())
                        return true;

                    if (TryResolveLocalValue(
                            localReference.Local,
                            localReference,
                            localReference.FindOwningExecutableRoot(),
                            localValueCache,
                            cancellationToken,
                            out var resolvedValue))
                    {
                        current = resolvedValue;
                        continue;
                    }

                    return false;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryResolveLocalValue(
        ILocalSymbol local,
        IOperation reference,
        IOperation? executableRoot,
        LocalValueCache localValueCache,
        CancellationToken cancellationToken,
        out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        return localValueCache.TryGetLatestValue(
            executableRoot,
            local,
            reference.Syntax.SpanStart,
            cancellationToken,
            out value);
    }

    private sealed class LocalValueCache
    {
        private readonly object syncRoot = new();
        private readonly Dictionary<IOperation, Dictionary<ILocalSymbol, List<LocalWrite>>> writesByRoot = new();

        public bool TryGetLatestValue(
            IOperation executableRoot,
            ILocalSymbol local,
            int referenceStart,
            CancellationToken cancellationToken,
            out IOperation value)
        {
            value = null!;
            var writes = GetWrites(executableRoot, cancellationToken);
            if (!writes.TryGetValue(local, out var localWrites))
                return false;

            var bestWriteStart = -1;

            foreach (var write in localWrites)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (write.SpanStart >= referenceStart || write.SpanStart <= bestWriteStart)
                    continue;

                bestWriteStart = write.SpanStart;
                value = write.Value;
            }

            return bestWriteStart >= 0;
        }

        private Dictionary<ILocalSymbol, List<LocalWrite>> GetWrites(
            IOperation executableRoot,
            CancellationToken cancellationToken)
        {
            lock (syncRoot)
            {
                if (!writesByRoot.TryGetValue(executableRoot, out var writes))
                {
                    writes = BuildWrites(executableRoot, cancellationToken);
                    writesByRoot.Add(executableRoot, writes);
                }

                return writes;
            }
        }

        private static Dictionary<ILocalSymbol, List<LocalWrite>> BuildWrites(
            IOperation executableRoot,
            CancellationToken cancellationToken)
        {
            var writes = new Dictionary<ILocalSymbol, List<LocalWrite>>(SymbolEqualityComparer.Default);

            foreach (var descendant in executableRoot.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (descendant is IVariableDeclarationOperation declaration)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer == null)
                            continue;

                        AddWrite(writes, declarator.Symbol, declarator.Syntax.SpanStart, declarator.Initializer.Value);
                    }
                }

                if (descendant is ISimpleAssignmentOperation assignment &&
                    assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal)
                {
                    AddWrite(writes, targetLocal.Local, assignment.Syntax.SpanStart, assignment.Value);
                }
            }

            return writes;
        }

        private static void AddWrite(
            Dictionary<ILocalSymbol, List<LocalWrite>> writes,
            ILocalSymbol local,
            int spanStart,
            IOperation value)
        {
            if (!writes.TryGetValue(local, out var localWrites))
            {
                localWrites = new List<LocalWrite>();
                writes.Add(local, localWrites);
            }

            localWrites.Add(new LocalWrite(spanStart, value));
        }
    }

    private readonly struct LocalWrite
    {
        public LocalWrite(int spanStart, IOperation value)
        {
            SpanStart = spanStart;
            Value = value;
        }

        public int SpanStart { get; }

        public IOperation Value { get; }
    }

    private static Location GetMethodLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax invocationSyntax &&
            invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.GetLocation();
        }

        return invocation.Syntax.GetLocation();
    }
}
