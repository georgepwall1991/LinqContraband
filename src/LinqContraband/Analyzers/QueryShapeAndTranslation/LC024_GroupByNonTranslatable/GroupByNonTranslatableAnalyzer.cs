using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC024_GroupByNonTranslatable;

/// <summary>
/// Detects Select projections on GroupBy results that access group elements in non-translatable ways.
/// EF Core can only translate g.Key and aggregate functions (Count, Sum, Average, Min, Max) server-side. Diagnostic ID: LC024
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GroupByNonTranslatableAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC024";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "GroupBy with Non-Translatable Projection";

    private static readonly LocalizableString MessageFormat =
        "Accessing group elements with '{0}' cannot be translated to SQL. Use only Key and aggregate functions (Count, Sum, Average, Min, Max).";

    private static readonly LocalizableString Description =
        "EF Core can only translate g.Key and aggregate functions (Count, Sum, Average, Min, Max, LongCount) in GroupBy projections. Other accesses force client-side evaluation or throw.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC024_GroupByNonTranslatable.md");

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

        if (method.Name != "Select") return;

        // Check if receiver type is IQueryable
        var receiverType = invocation.GetInvocationReceiverType();
        if (!receiverType.IsIQueryable()) return;

        // Check if the IQueryable's element type is IGrouping<,>
        if (!IsGroupingQueryable(receiverType)) return;

        // Find the lambda argument
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value;
            while (value is IConversionOperation conv) value = conv.Operand;
            while (value is IDelegateCreationOperation del) value = del.Target;

            if (value is not IAnonymousFunctionOperation lambda) continue;

            // The lambda parameter represents the grouping (g)
            if (lambda.Symbol.Parameters.Length == 0) continue;
            var groupParam = lambda.Symbol.Parameters[0];

            // Walk the lambda body and check all references to the grouping parameter
            CheckOperationForNonTranslatableAccess(lambda.Body, groupParam, context, invocation);
        }
    }

    private static bool IsGroupingQueryable(ITypeSymbol? type)
    {
        if (type == null) return false;

        var elementType = GetQueryableElementType(type);
        if (elementType == null) return false;

        return IsGrouping(elementType);
    }

    private static ITypeSymbol? GetQueryableElementType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.Name == "IQueryable" && named.ContainingNamespace?.ToString() == "System.Linq")
        {
            return named.TypeArguments.Length > 0 ? named.TypeArguments[0] : null;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IQueryable" &&
                iface.ContainingNamespace?.ToString() == "System.Linq" &&
                iface.TypeArguments.Length > 0)
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsGrouping(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            if (named.Name == "IGrouping" && named.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType && iface.Name == "IGrouping" &&
                iface.ContainingNamespace?.ToString() == "System.Linq")
                return true;
        }

        return false;
    }

    private void CheckOperationForNonTranslatableAccess(
        IOperation operation,
        IParameterSymbol groupParam,
        OperationAnalysisContext context,
        IInvocationOperation selectInvocation)
    {
        foreach (var invocation in GetAllOperations(operation).OfType<IInvocationOperation>())
        {
            if (IsTranslatableGroupAccess(invocation, groupParam))
                continue;

            if (!invocation.ReferencesParameter(groupParam))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
            return;
        }

        foreach (var descendant in GetAllOperations(operation))
        {
            if (descendant is IParameterReferenceOperation paramRef &&
                SymbolEqualityComparer.Default.Equals(paramRef.Parameter, groupParam))
            {
                var usage = paramRef.Parent;

                // Unwrap conversions
                while (usage is IConversionOperation)
                    usage = usage.Parent;

                // Allow g.Key
                if (usage is IPropertyReferenceOperation propRef && propRef.Property.Name == "Key")
                    continue;

                // Allow aggregate methods: g.Count(), g.Sum(), g.Average(), g.Min(), g.Max(), g.LongCount()
                if (usage is IArgumentOperation argOp && argOp.Parent is IInvocationOperation aggInvocation)
                {
                    if (IsTranslatableGroupAccess(aggInvocation, groupParam))
                        continue;

                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, aggInvocation.Syntax.GetLocation(), aggInvocation.TargetMethod.Name));
                    return;
                }

                if (usage is IInvocationOperation directInvocation)
                {
                    if (IsTranslatableGroupAccess(directInvocation, groupParam))
                        continue;

                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, directInvocation.Syntax.GetLocation(), directInvocation.TargetMethod.Name));
                    return;
                }

                // Any other usage of g is non-translatable
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, paramRef.Syntax.GetLocation(), "direct access"));
                return;
            }
        }
    }

    // Group operators that EF Core can translate as part of a server-side aggregate chain
    // (e.g. g.Where(p).Count(), g.Select(s).Sum(), g.Distinct().Count()).
    private static readonly ImmutableHashSet<string> TranslatableGroupOperators = ImmutableHashSet.Create(
        "Where", "Select", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Distinct");

    private static bool IsAllowedAggregateMethod(string methodName)
    {
        return methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max" or "Any" or "All"
            or "CountAsync" or "LongCountAsync" or "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync"
            or "AnyAsync" or "AllAsync";
    }

    // A group-access invocation is translatable when its receiver chain roots at the grouping
    // parameter through translatable operators AND the OUTERMOST invocation of that chain is an
    // allowed aggregate. EF Core 9 translates filtered/projected group aggregates such as
    // g.Where(p).Count() and g.Select(s).Sum(), but a chain that terminates in a non-aggregate
    // (a bare g.Where(p), a materializer g.Select(s).ToList(), or an element accessor
    // g.OrderBy(s).First()) still returns a sub-sequence or materializes and must be reported.
    private static bool IsTranslatableGroupAccess(IInvocationOperation invocation, IParameterSymbol groupParam)
    {
        if (!RootsAtGroupParam(invocation.GetInvocationReceiver(), groupParam))
            return false;

        var terminal = FindOutermostGroupChainInvocation(invocation);
        if (!IsAllowedAggregateMethod(terminal.TargetMethod.Name) ||
            !IsKnownAggregateContainingType(terminal.TargetMethod.ContainingType))
        {
            return false;
        }

        // The chain operators are translatable, but their predicate/selector lambda bodies must be
        // too. Rather than guess which BCL/user method calls EF can translate, this stays
        // deliberately conservative: the chain is exempt only when its lambda bodies are
        // invocation-free (member access, comparisons, arithmetic). ANY method call inside a
        // predicate/selector — a local function, a user method, or a non-translatable BCL overload
        // such as o.Name.Equals(s, StringComparison.OrdinalIgnoreCase) or Regex.IsMatch(...) —
        // keeps the chain reported. The terminal subtree contains every lambda in the chain.
        return !ChainHasLambdaInvocation(terminal, groupParam);
    }

    // True when the group-chain subtree contains an invocation that is NOT one of the chain's own
    // translatable group operators rooted at the grouping parameter — i.e. a method call inside a
    // predicate/selector lambda (or a nested non-group invocation). EF Core cannot be assumed to
    // translate such calls, so the chain is reported.
    private static bool ChainHasLambdaInvocation(IInvocationOperation terminal, IParameterSymbol groupParam)
    {
        foreach (var descendant in GetAllOperations(terminal).OfType<IInvocationOperation>())
        {
            if (!IsGroupChainMethod(descendant.TargetMethod) ||
                !RootsAtGroupParam(descendant.GetInvocationReceiver(), groupParam))
            {
                return true;
            }
        }

        return false;
    }

    // Walks the receiver chain and returns true only if it bottoms out at the grouping parameter,
    // passing exclusively through translatable group operators / aggregates.
    private static bool RootsAtGroupParam(IOperation? receiver, IParameterSymbol groupParam)
    {
        var current = receiver;
        while (current != null)
        {
            current = current.UnwrapConversions();
            if (current is IParameterReferenceOperation parameterReference)
                return SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, groupParam);

            if (current is IInvocationOperation chained && IsGroupChainMethod(chained.TargetMethod))
            {
                current = chained.GetInvocationReceiver();
                continue;
            }

            return false;
        }

        return false;
    }

    // Walks upward through enclosing group-chain invocations (where the current node is the
    // receiver) to find the chain's terminal invocation.
    private static IInvocationOperation FindOutermostGroupChainInvocation(IInvocationOperation invocation)
    {
        var outermost = invocation;
        while (true)
        {
            IOperation? parent = outermost.Parent;
            while (parent is IConversionOperation or IArgumentOperation)
                parent = parent.Parent;

            if (parent is IInvocationOperation parentInvocation &&
                IsGroupChainMethod(parentInvocation.TargetMethod) &&
                ReferenceEquals(parentInvocation.GetInvocationReceiver()?.UnwrapConversions(), outermost))
            {
                outermost = parentInvocation;
                continue;
            }

            return outermost;
        }
    }

    private static bool IsGroupChainMethod(IMethodSymbol method)
    {
        return (IsAllowedAggregateMethod(method.Name) || TranslatableGroupOperators.Contains(method.Name)) &&
               IsKnownAggregateContainingType(method.ContainingType);
    }

    private static bool IsKnownAggregateContainingType(INamedTypeSymbol? containingType)
    {
        var containingNamespace = containingType?.ContainingNamespace?.ToString();
        return containingNamespace == "System.Linq" && containingType is { Name: "Enumerable" or "Queryable" } ||
               containingNamespace == "Microsoft.EntityFrameworkCore" && containingType?.Name == "EntityFrameworkQueryableExtensions";
    }

    private static System.Collections.Generic.IEnumerable<IOperation> GetAllOperations(IOperation root)
    {
        yield return root;
        foreach (var child in root.ChildOperations)
        {
            foreach (var descendant in GetAllOperations(child))
                yield return descendant;
        }
    }
}
