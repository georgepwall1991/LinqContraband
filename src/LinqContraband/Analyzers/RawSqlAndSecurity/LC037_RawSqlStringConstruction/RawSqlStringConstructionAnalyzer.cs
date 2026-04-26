using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RawSqlStringConstructionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC037";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid constructed raw SQL strings";

    private static readonly LocalizableString MessageFormat =
        "The SQL passed to '{0}' is built from string construction and should be parameterized instead";

    private static readonly LocalizableString Description =
        "Raw SQL APIs should receive constant SQL text plus parameters. String.Format, String.Concat, StringBuilder, and aliased string construction all hide injection risk.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC037_RawSqlStringConstruction.md");

    private static readonly ImmutableHashSet<string> TargetMethods = ImmutableHashSet.Create(
        "FromSqlRaw",
        "ExecuteSqlRaw",
        "ExecuteSqlRawAsync");

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

        if (!IsTargetRawSqlMethod(method))
            return;

        var sqlArgument = GetSqlArgument(invocation, method);
        if (sqlArgument == null)
            return;

        var executableRoot = invocation.FindOwningExecutableRoot();
        if (IsOwnedBySpecificRawSqlAnalyzer(sqlArgument.Value, executableRoot))
            return;

        if (!IsConstructedRawSql(sqlArgument.Value, executableRoot))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Value.Syntax.GetLocation(), method.Name));
    }

    private static bool IsTargetRawSqlMethod(IMethodSymbol method)
    {
        return TargetMethods.Contains(method.Name) &&
               method.ContainingNamespace?.ToString().StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }

    private static IArgumentOperation? GetSqlArgument(IInvocationOperation invocation, IMethodSymbol method)
    {
        var sqlParameterIndex = -1;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Name == "sql")
            {
                sqlParameterIndex = i;
                break;
            }
        }

        if (sqlParameterIndex < 0 || sqlParameterIndex >= invocation.Arguments.Length)
            return null;

        return invocation.Arguments[sqlParameterIndex];
    }
}
