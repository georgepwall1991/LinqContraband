using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidExecuteSqlRawWithInterpolationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC034";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid ExecuteSqlRaw with interpolated strings";

    private static readonly LocalizableString MessageFormat =
        "Use 'ExecuteSql' instead of '{0}' when passing interpolated strings or concatenated SQL";

    private static readonly LocalizableString Description =
        "Raw SQL execution APIs should receive constant SQL text plus parameters. Interpolated strings and concatenation are safer through ExecuteSql/ExecuteSqlAsync.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC034_AvoidExecuteSqlRawWithInterpolation.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!IsTargetMethod(method))
            return;

        var sqlArgument = GetSqlArgument(invocation, method);
        if (sqlArgument == null)
            return;

        if (!IsPotentiallyUnsafeSql(sqlArgument.Value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Value.Syntax.GetLocation(), method.Name));
    }

    private static bool IsTargetMethod(IMethodSymbol method)
    {
        return (method.Name == "ExecuteSqlRaw" || method.Name == "ExecuteSqlRawAsync") &&
               method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }

    private static IArgumentOperation? GetSqlArgument(IInvocationOperation invocation, IMethodSymbol method)
    {
        var sqlParameterIndex = method.Parameters.ToList().FindIndex(parameter => parameter.Name == "sql");
        if (sqlParameterIndex < 0 || sqlParameterIndex >= invocation.Arguments.Length)
            return null;

        return invocation.Arguments[sqlParameterIndex];
    }

    private static bool IsPotentiallyUnsafeSql(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation)
            return true;

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
            return IsUnsafeConcatenation(binary);

        return false;
    }

    private static bool IsUnsafeConcatenation(IBinaryOperation binary)
    {
        return IsUnsafeSide(binary.LeftOperand) || IsUnsafeSide(binary.RightOperand);
    }

    private static bool IsUnsafeSide(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current.ConstantValue.HasValue)
            return false;

        if (current is IBinaryOperation nestedBinary && nestedBinary.OperatorKind == BinaryOperatorKind.Add)
            return IsUnsafeConcatenation(nestedBinary);

        return true;
    }
}
