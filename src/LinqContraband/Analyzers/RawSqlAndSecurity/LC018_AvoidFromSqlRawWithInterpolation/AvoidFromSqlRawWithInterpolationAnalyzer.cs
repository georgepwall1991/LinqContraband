using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Catalog;
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
        "Using interpolated strings with FromSqlRaw can lead to SQL injection. Use FromSql (FromSqlInterpolated before EF Core 7) or SqlQuery for safe parameterization.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC018_AvoidFromSqlRawWithInterpolation.html");

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

        var sqlArgument = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "sql");

        if (sqlArgument == null || !IsPotentiallyUnsafe(sqlArgument.Value))
            return;

        // EF Core 8+ reports the same call as EF1002 (and EF Core 10+ the concatenation as EF1003),
        // with its own FromSql/SqlQuery fix, so a second warning on the same line adds only noise.
        if (EfCoreRawSqlAnalyzerOverlap.IsReportedByEfCoreAnalyzer(invocation, sqlArgument) &&
            EfCoreRawSqlAnalyzerOverlap.DefersToEfCoreAnalyzers(context.Options, invocation.Syntax.SyntaxTree, DiagnosticId))
            return;

        var safeAlternative = method.Name == "SqlQueryRaw" ? "SqlQuery" : GetSafeFromSqlName(method);
        context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Value.Syntax.GetLocation(), safeAlternative, method.Name));
    }

    /// <summary>
    /// The parameterizing sibling of <paramref name="fromSqlRaw"/> declared next to it: <c>FromSql</c>
    /// (EF Core 7+, and the only one on Cosmos), else <c>FromSqlInterpolated</c>. Obsolete members are
    /// skipped because EF Core 11 marks <c>FromSqlInterpolated</c> obsolete.
    /// </summary>
    internal static string GetSafeFromSqlName(IMethodSymbol fromSqlRaw)
    {
        var containingType = fromSqlRaw.ContainingType;
        if (containingType is null)
            return "FromSql";

        if (HasFormattableSqlOverload(containingType, "FromSql"))
            return "FromSql";

        return HasFormattableSqlOverload(containingType, "FromSqlInterpolated") ? "FromSqlInterpolated" : "FromSql";
    }

    private static bool HasFormattableSqlOverload(INamedTypeSymbol containingType, string name)
    {
        foreach (var member in containingType.GetMembers(name))
        {
            if (member is IMethodSymbol { Parameters.Length: >= 2 } candidate &&
                candidate.Parameters[1].Type.Name == "FormattableString" &&
                candidate.Parameters[1].Type.ContainingNamespace?.ToDisplayString() == "System" &&
                !candidate.IsObsolete())
                return true;
        }

        return false;
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
