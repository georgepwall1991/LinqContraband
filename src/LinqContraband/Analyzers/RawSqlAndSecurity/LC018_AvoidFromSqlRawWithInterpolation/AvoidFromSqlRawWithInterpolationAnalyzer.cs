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
public sealed partial class AvoidFromSqlRawWithInterpolationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC018";
    private const string Category = "Security";
    private static readonly LocalizableString Title = "Avoid FromSqlRaw with interpolated strings";

    private static readonly LocalizableString MessageFormat =
        "Use '{0}' instead of '{1}' when using interpolated strings or non-constant concatenations to prevent SQL injection";

    private static readonly LocalizableString Description =
        "Using interpolated strings with FromSqlRaw can lead to SQL injection. Use FromSqlInterpolated for safe parameterization.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC018_AvoidFromSqlRawWithInterpolation.md");

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

        // FromSqlRaw is the IQueryable/DbSet raw-SQL entry point; SqlQueryRaw is its scalar/keyless
        // twin on the DatabaseFacade (db.Database.SqlQueryRaw<T>(...)). Both take a raw `string sql`
        // and are equal injection sinks; their safe siblings are FromSqlInterpolated and SqlQuery
        // (FormattableString), which are not flagged.
        if (method.Name != "FromSqlRaw" && method.Name != "SqlQueryRaw") return;

        // Verify it's an EF Core method
        if (!IsEfCoreMethod(method)) return;

        var receiverType = invocation.GetInvocationReceiverType();
        if (receiverType?.IsIQueryable() != true &&
            receiverType?.IsDbSet() != true &&
            !IsDatabaseFacade(receiverType))
            return;

        var sqlArgument = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "sql")?.Value;

        if (sqlArgument != null && IsPotentiallyUnsafe(sqlArgument))
        {
            var safeAlternative = method.Name == "SqlQueryRaw" ? "SqlQuery" : "FromSqlInterpolated";
            context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Syntax.GetLocation(), safeAlternative, method.Name));
        }
    }

    private static bool IsDatabaseFacade(ITypeSymbol? type)
    {
        return type?.Name == "DatabaseFacade" &&
               type.ContainingNamespace?.ToString()?.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal) == true;
    }

    private bool IsEfCoreMethod(IMethodSymbol method)
    {
        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

}
