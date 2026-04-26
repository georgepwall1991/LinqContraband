using System.Collections.Immutable;
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
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
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

        if (!HasEntityFrameworkQuerySource(receiver))
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

    private static bool HasEntityFrameworkQuerySource(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
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
                    if (localReference.Type.IsDbSet())
                        return true;

                    if (TryResolveLocalValue(localReference.Local, localReference, localReference.FindOwningExecutableRoot(), out var resolvedValue))
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

    private static bool TryResolveLocalValue(ILocalSymbol local, IOperation reference, IOperation? executableRoot, out IOperation value)
    {
        value = null!;

        if (executableRoot == null)
            return false;

        var referenceStart = reference.Syntax.SpanStart;
        var bestWriteStart = -1;

        foreach (var descendant in executableRoot.Descendants())
        {
            if (descendant is IVariableDeclarationOperation declaration)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (!SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) || declarator.Initializer == null)
                        continue;

                    var writeStart = declarator.Syntax.SpanStart;
                    if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                        continue;

                    bestWriteStart = writeStart;
                    value = declarator.Initializer.Value;
                }
            }

            if (descendant is ISimpleAssignmentOperation assignment &&
                assignment.Target.UnwrapConversions() is ILocalReferenceOperation targetLocal &&
                SymbolEqualityComparer.Default.Equals(targetLocal.Local, local))
            {
                var writeStart = assignment.Syntax.SpanStart;
                if (writeStart >= referenceStart || writeStart <= bestWriteStart)
                    continue;

                bestWriteStart = writeStart;
                value = assignment.Value;
            }
        }

        return bestWriteStart >= 0;
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
