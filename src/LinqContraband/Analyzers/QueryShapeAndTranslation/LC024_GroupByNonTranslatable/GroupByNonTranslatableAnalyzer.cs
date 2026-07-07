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
public sealed partial class GroupByNonTranslatableAnalyzer : DiagnosticAnalyzer
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

        if (method.Name == "Select")
        {
            AnalyzeSelectProjection(invocation, context);
            return;
        }

        if (method.Name == "GroupBy")
        {
            AnalyzeGroupByResultSelector(invocation, context);
        }
    }

    private void AnalyzeSelectProjection(IInvocationOperation invocation, OperationAnalysisContext context)
    {
        // Check if receiver type is IQueryable
        var receiverType = invocation.GetInvocationReceiverType();
        if (!receiverType.IsIQueryable()) return;

        // Check if the IQueryable's element type is IGrouping<,>
        if (!IsGroupingQueryable(receiverType)) return;

        foreach (var lambda in GetAnonymousFunctionArguments(invocation))
        {
            // The lambda parameter represents the grouping (g)
            if (lambda.Symbol.Parameters.Length == 0) continue;
            var groupParam = lambda.Symbol.Parameters[0];

            CheckOperationForNonTranslatableAccess(lambda.Body, groupParam, context);
        }
    }

    private void AnalyzeGroupByResultSelector(IInvocationOperation invocation, OperationAnalysisContext context)
    {
        var receiverType = invocation.GetInvocationReceiverType();
        if (!receiverType.IsIQueryable()) return;

        foreach (var lambda in GetAnonymousFunctionArguments(invocation))
        {
            // Queryable.GroupBy result selectors have (key, group) parameters.
            if (lambda.Symbol.Parameters.Length < 2) continue;
            var groupParam = lambda.Symbol.Parameters[1];

            CheckOperationForNonTranslatableAccess(lambda.Body, groupParam, context);
        }
    }

    private static System.Collections.Generic.IEnumerable<IAnonymousFunctionOperation> GetAnonymousFunctionArguments(
        IInvocationOperation invocation)
    {
        foreach (var arg in invocation.Arguments)
        {
            var value = arg.Value;
            while (value is IConversionOperation conv) value = conv.Operand;
            while (value is IDelegateCreationOperation del) value = del.Target;

            if (value is IAnonymousFunctionOperation lambda)
                yield return lambda;
        }
    }
}
