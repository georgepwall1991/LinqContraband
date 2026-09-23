using System;
using System.Collections.Immutable;
using System.Threading;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC054_MigrateInsideTransaction;

/// <summary>
/// Analyzes <c>Database.Migrate()</c> / <c>MigrateAsync()</c> calls made while a transaction the code started on the
/// same context is still open. Diagnostic ID: LC054
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> from EF Core 9, migrations manage their own transaction, execution strategy and
/// database lock. When the context already has a transaction from <c>BeginTransaction</c>, <c>Migrate</c> raises
/// <c>MigrationsUserTransactionWarning</c>, which EF Core treats as an error by default, so the app fails at startup.
/// Wrapping migrations in a transaction was the recommended resilient pattern before EF Core 9.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrateInsideTransactionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC054";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "Migrate called inside a user transaction";

    private static readonly LocalizableString MessageFormat =
        "'{0}' runs inside the transaction from '{1}' on the same context; EF Core 9 and later throw instead of applying migrations";

    private static readonly LocalizableString Description =
        "From EF Core 9, Migrate and MigrateAsync start their own transaction and take a migration lock. A transaction already open on the context raises MigrationsUserTransactionWarning, which is an error by default. Call Migrate without the surrounding transaction.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC054_MigrateInsideTransaction.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        // Only EF Core 9 and later refuse a user transaction; before that it was the documented pattern.
        var eventIds = context.Compilation.GetTypeByMetadataName(MigrateInsideTransactionScope.RelationalEventIdTypeName);
        if (eventIds == null || eventIds.GetMembers(MigrateInsideTransactionScope.WarningName).IsEmpty) return;

        var compilation = context.Compilation;
        var cancellationToken = context.CancellationToken;
        var warningConfigured = new Lazy<bool>(
            () => MigrateInsideTransactionScope.IsWarningDowngraded(compilation, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);

        context.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, warningConfigured), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, Lazy<bool> warningConfigured)
    {
        var migrate = (IInvocationOperation)context.Operation;
        if (!MigrateInsideTransactionScope.TryGetMigrateFacade(migrate, out var facade)) return;

        var contextRoot = MigrateInsideTransactionScope.GetContextRoot(facade);
        if (contextRoot == null) return;

        var match = MigrateInsideTransactionScope.FindOpenTransaction(migrate, contextRoot);
        if (match == null || warningConfigured.Value) return;

        var location = migrate.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : migrate.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location,
            ImmutableArray.Create(match.Begin.Syntax.GetLocation()),
            migrate.TargetMethod.Name,
            match.Begin.TargetMethod.Name));
    }
}
