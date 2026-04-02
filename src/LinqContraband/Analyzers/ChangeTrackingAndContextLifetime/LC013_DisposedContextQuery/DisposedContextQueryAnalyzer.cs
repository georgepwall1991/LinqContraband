using System.Collections.Generic;
using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC013_DisposedContextQuery;

/// <summary>
/// Analyzes deferred LINQ queries (IQueryable/IAsyncEnumerable) that are returned from disposed DbContext instances. Diagnostic ID: LC013
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> LINQ queries against DbContext are deferred and only execute when enumerated. Returning
/// an unenumerated query from a method where the DbContext is disposed (using statement or using declaration) will cause
/// runtime errors when the query is eventually executed. Queries should be materialized (ToList, ToArray, etc.) before
/// the context is disposed.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class DisposedContextQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC013";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Disposed Context Query";

    private static readonly LocalizableString MessageFormat =
        "The query is built from DbContext '{0}' which is disposed before enumeration. Materialize before returning.";

    private static readonly LocalizableString Description =
        "Returning a deferred query from a disposed context causes runtime errors.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeReturn, OperationKind.Return);
    }

    private void AnalyzeReturn(OperationAnalysisContext context)
    {
        var returnOp = (IReturnOperation)context.Operation;
        var returnedValue = returnOp.ReturnedValue?.UnwrapConversions();

        if (returnedValue == null)
            return;

        var executableRoot = returnOp.FindOwningExecutableRoot();
        if (!IsSupportedExecutableRoot(executableRoot))
            return;

        if (!IsDeferredType(returnedValue.Type))
            return;

        CheckExpression(returnedValue, executableRoot!, context);
    }

    private void CheckExpression(IOperation? operation, IOperation executableRoot, OperationAnalysisContext context)
    {
        if (operation == null)
            return;

        operation = operation.UnwrapConversions();

        if (operation is IConditionalOperation conditional)
        {
            CheckExpression(conditional.WhenTrue, executableRoot, context);
            CheckExpression(conditional.WhenFalse, executableRoot, context);
            return;
        }

        if (operation is ICoalesceOperation coalesce)
        {
            CheckExpression(coalesce.Value, executableRoot, context);
            CheckExpression(coalesce.WhenNull, executableRoot, context);
            return;
        }

        if (operation is ISwitchExpressionOperation switchExpr)
        {
            foreach (var arm in switchExpr.Arms)
                CheckExpression(arm.Value, executableRoot, context);
            return;
        }

        if (TryResolveDisposedContextOrigin(
                operation,
                executableRoot,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out var dbContextLocal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, operation.Syntax.GetLocation(), dbContextLocal.Name));
        }
    }
}
