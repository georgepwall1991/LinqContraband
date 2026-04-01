using System.Collections.Immutable;
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
                    if (IsAllowedAggregateMethod(aggInvocation.TargetMethod.Name))
                        continue;

                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, aggInvocation.Syntax.GetLocation(), aggInvocation.TargetMethod.Name));
                    return;
                }

                if (usage is IInvocationOperation directInvocation)
                {
                    if (IsAllowedAggregateMethod(directInvocation.TargetMethod.Name))
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

    private static bool IsAllowedAggregateMethod(string methodName)
    {
        return methodName is "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max"
            or "CountAsync" or "LongCountAsync" or "SumAsync" or "AverageAsync" or "MinAsync" or "MaxAsync";
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
