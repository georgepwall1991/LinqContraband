using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC003_AnyOverCount;

/// <summary>
/// Analyzes existence checks using Count() instead of Any() on IQueryable collections. Diagnostic ID: LC003
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Using Count() > 0 on an IQueryable forces the database to count all matching records,
/// even when you only need to know if at least one exists. The Any() method is optimized to return as soon as the first
/// matching record is found, translating to SQL that uses EXISTS or TOP(1), which is significantly faster. This becomes
/// especially critical with large result sets where Count() might scan millions of rows unnecessarily.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class AnyOverCountAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC003";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Prefer Any() over Count() existence checks";

    private static readonly LocalizableString MessageFormat =
        "Use Any() or !Any() instead of Count()-based existence checks on IQueryable";

    private static readonly LocalizableString Description =
        "Checking if Count() is greater than 0 on an IQueryable can be expensive as it may iterate the entire result set. Any() is optimized to return as soon as a match is found.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC003_AnyOverCount.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeBinaryOperator, OperationKind.Binary);
    }

    private void AnalyzeBinaryOperator(OperationAnalysisContext context)
    {
        var binaryOp = (IBinaryOperation)context.Operation;

        if (!TryGetCountExistenceCheck(binaryOp, out var countInvocation)) return;

        // Unwrap implicit conversions or await operations
        while (true)
        {
            if (countInvocation is IConversionOperation conversion)
            {
                countInvocation = conversion.Operand;
            }
            else if (countInvocation is IAwaitOperation awaitOp)
            {
                countInvocation = awaitOp.Operation;
            }
            else
            {
                break;
            }
        }

        if (countInvocation is IInvocationOperation invocation)
        {
            var method = invocation.TargetMethod;

            var isSyncCount = (method.Name == "Count" || method.Name == "LongCount") &&
                              method.ContainingType.Name == "Queryable" &&
                              method.ContainingNamespace?.ToString() == "System.Linq";

            var isAsyncCount = (method.Name == "CountAsync" || method.Name == "LongCountAsync") &&
                               method.ContainingType.Name == "EntityFrameworkQueryableExtensions" &&
                               method.ContainingNamespace?.ToString() == "Microsoft.EntityFrameworkCore";

            if (isSyncCount || isAsyncCount)
            {
                // Check if the source is IQueryable
                var receiverType = invocation.GetInvocationReceiverType();

                if (receiverType?.IsIQueryable() == true)
                    context.ReportDiagnostic(Diagnostic.Create(Rule, binaryOp.Syntax.GetLocation()));
            }
        }
    }
}
