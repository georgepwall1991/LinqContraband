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
        "Use '{0}' instead of '{1}' when passing interpolated strings or concatenated SQL";

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
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC034_AvoidExecuteSqlRawWithInterpolation.md");

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

        if (!IsDatabaseFacadeReceiver(invocation.GetInvocationReceiverType()))
            return;

        var sqlArgument = GetSqlArgument(invocation, method);
        if (sqlArgument == null)
            return;

        if (!IsPotentiallyUnsafeSql(sqlArgument.Value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            sqlArgument.Value.Syntax.GetLocation(),
            GetReplacementMethodName(method.Name),
            method.Name));
    }

    private static bool IsTargetMethod(IMethodSymbol method)
    {
        return (method.Name == "ExecuteSqlRaw" || method.Name == "ExecuteSqlRawAsync") &&
               IsEfCoreNamespace(method.ContainingNamespace);
    }

    private static bool IsEfCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsDatabaseFacadeReceiver(ITypeSymbol? type)
    {
        return type?.Name == "DatabaseFacade" &&
               IsEfCoreNamespace(type.ContainingNamespace);
    }

    private static string GetReplacementMethodName(string methodName)
    {
        return methodName == "ExecuteSqlRawAsync" ? "ExecuteSqlAsync" : "ExecuteSql";
    }

    private static IArgumentOperation? GetSqlArgument(IInvocationOperation invocation, IMethodSymbol method)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "sql")
                return argument;
        }

        var sqlParameterIndex = method.Parameters.ToList().FindIndex(parameter => parameter.Name == "sql");
        return sqlParameterIndex >= 0 && sqlParameterIndex < invocation.Arguments.Length
            ? invocation.Arguments[sqlParameterIndex]
            : null;
    }

    private static bool IsPotentiallyUnsafeSql(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        if (current is IInterpolatedStringOperation interpolatedString)
            return HasNonConstantInterpolation(interpolatedString);

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

    private static bool HasNonConstantInterpolation(IInterpolatedStringOperation interpolatedString)
    {
        return interpolatedString.Parts
            .OfType<IInterpolationOperation>()
            .Any(interpolation => !interpolation.Expression.UnwrapConversions().ConstantValue.HasValue);
    }
}
