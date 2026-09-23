using System.Collections.Immutable;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC056_StoredProcedureComposed;

/// <summary>
/// Analyzes raw SQL queries that call a stored procedure (<c>EXEC</c>) and then apply LINQ operators that EF Core has
/// to translate into SQL around that call. Diagnostic ID: LC056
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> EF Core composes LINQ over raw SQL by wrapping it in a subquery, and a stored
/// procedure call cannot be a subquery. EF Core throws "FromSql or SqlQuery was called with non-composable SQL and
/// with a query composing over it" at run time. Switch to client-side evaluation with <c>AsEnumerable()</c> or
/// <c>AsAsyncEnumerable()</c> right after the raw SQL call.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StoredProcedureComposedAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC056";
    private const string Category = "Correctness";
    private static readonly LocalizableString Title = "LINQ composed over a stored procedure call";

    private static readonly LocalizableString MessageFormat =
        "'{0}' composes over the stored procedure call in '{1}'; EF Core cannot wrap EXEC in a subquery and throws at run time";

    private static readonly LocalizableString Description =
        "EF Core applies LINQ operators after FromSql, FromSqlRaw, SqlQuery and similar calls by wrapping the SQL in a subquery. A stored procedure call (EXEC) cannot be wrapped, so EF Core throws at run time. Call AsEnumerable() or AsAsyncEnumerable() right after the raw SQL call to run the rest of the query in memory.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC056_StoredProcedureComposed.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var composer = (IInvocationOperation)context.Operation;
        var source = StoredProcedureQuery.FindComposedStoredProcedureCall(composer);
        if (source == null) return;

        var location = composer.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : composer.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, composer.TargetMethod.Name, source.TargetMethod.Name));
    }
}
