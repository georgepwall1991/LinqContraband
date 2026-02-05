using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

/// <summary>
/// Analyzes FromSqlRaw usage to detect potential SQL injection vulnerabilities from interpolated strings or non-constant concatenations. Diagnostic ID: LC018
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidFromSqlRawWithInterpolationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC018";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid FromSqlRaw with interpolated strings";

    private static readonly LocalizableString MessageFormat =
        "Use 'FromSqlInterpolated' instead of 'FromSqlRaw' when using interpolated strings or non-constant concatenations to prevent SQL injection";

    private static readonly LocalizableString Description =
        "Using interpolated strings with FromSqlRaw can lead to SQL injection. Use FromSqlInterpolated for safe parameterization.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC018_AvoidFromSqlRawWithInterpolation.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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

        if (method.Name != "FromSqlRaw") return;

        // Verify it's an EF Core method
        if (!IsEfCoreMethod(method)) return;

        // Find the 'sql' parameter index
        var sqlParameterIndex = method.Parameters.ToList().FindIndex(p => p.Name == "sql");
        if (sqlParameterIndex < 0) return;

        // For extension methods called with instance syntax, the 'this' parameter is the first argument
        // but it is NOT included in the 'Parameters' list of the method if we look at it from the perspective of the call?
        // Actually, IInvocationOperation.Arguments aligns with Method.Parameters for extension methods too
        // IF it's an extension method call.

        if (sqlParameterIndex >= invocation.Arguments.Length) return;

        var sqlArgument = invocation.Arguments[sqlParameterIndex].Value;

        if (sqlArgument != null && IsPotentiallyUnsafe(sqlArgument))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Syntax.GetLocation()));
        }
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        return method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }

    private bool IsPotentiallyUnsafe(IOperation operation)
    {
        var current = operation;

        // Handle conversion to RawSqlString or other string-like types
        if (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }

        current = current.UnwrapConversions();

        if (current is IInterpolatedStringOperation)
        {
            // Even if it's all constants, FromSqlInterpolated is preferred if it's $""
            return true;
        }

        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
        {
            return IsUnsafeConcatenation(binary);
        }

        return false;
    }

    private bool IsUnsafeConcatenation(IBinaryOperation binary)
    {
        // Check left and right sides
        if (IsUnsafeSide(binary.LeftOperand)) return true;
        if (IsUnsafeSide(binary.RightOperand)) return true;
        return false;
    }

    private bool IsUnsafeSide(IOperation operation)
    {
        var current = operation.UnwrapConversions();

        // If it's a constant, it's safe
        if (current.ConstantValue.HasValue) return false;

        // If it's another concatenation, recurse
        if (current is IBinaryOperation binary && binary.OperatorKind == BinaryOperatorKind.Add)
        {
            return IsUnsafeConcatenation(binary);
        }

        // If it's a variable, parameter, or method call that is NOT a constant, it's unsafe for FromSqlRaw
        return true;
    }
}
