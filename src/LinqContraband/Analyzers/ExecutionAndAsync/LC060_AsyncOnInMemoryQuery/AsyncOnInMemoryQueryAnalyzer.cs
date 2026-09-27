using System.Collections.Immutable;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC060_AsyncOnInMemoryQuery;

/// <summary>
/// Analyzes EF Core async query operators called on an in-memory <c>AsQueryable()</c> query. Diagnostic ID: LC060
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> <c>Queryable.AsQueryable()</c> over a list or array returns an
/// <c>EnumerableQuery&lt;T&gt;</c>. It does not implement <c>IAsyncEnumerable&lt;T&gt;</c> and its provider is not
/// EF Core's <c>IAsyncQueryProvider</c>, so <c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>, <c>CountAsync</c> and the
/// other <c>EntityFrameworkQueryableExtensions</c> async operators throw <c>InvalidOperationException</c> ("The source
/// 'IQueryable' doesn't implement 'IAsyncEnumerable'") every time they run (dotnet/efcore#35666).</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncOnInMemoryQueryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC060";
    private const string Category = "Reliability";
    private static readonly LocalizableString Title = "EF Core async operator on an in-memory query";

    private static readonly LocalizableString MessageFormat =
        "'{0}' throws InvalidOperationException here: the query comes from AsQueryable() over an in-memory collection, which does not implement IAsyncEnumerable<T>";

    private static readonly LocalizableString Description =
        "EF Core's async query operators (ToListAsync, FirstOrDefaultAsync, CountAsync, AnyAsync, AsAsyncEnumerable, ...) only work on queries that EF Core runs. AsQueryable() over a list or array returns an EnumerableQuery, so they throw InvalidOperationException at run time. Use the synchronous operator, or query a DbSet.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC060_AsyncOnInMemoryQuery.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (compilationContext.Compilation.GetTypeByMetadataName(
                    "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions") == null)
                return;

            var provenance = new InMemoryQueryProvenance(compilationContext.Compilation, InMemoryQueryProof.AsyncIncapable);
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, provenance),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, InMemoryQueryProvenance provenance)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsEfCoreAsyncOperator(invocation.TargetMethod))
            return;

        if (!provenance.IsProvablyInMemory(invocation.GetInvocationReceiver()))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, GetNameLocation(invocation), invocation.TargetMethod.Name));
    }

    /// <summary>
    /// An <c>EntityFrameworkQueryableExtensions</c> operator that needs EF Core's async provider: every <c>...Async</c>
    /// method there, and <c>AsAsyncEnumerable</c>.
    /// </summary>
    private static bool IsEfCoreAsyncOperator(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.IsStatic &&
               original.Parameters.Length > 0 &&
               original.ContainingType is { Name: "EntityFrameworkQueryableExtensions" } containingType &&
               containingType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore" &&
               (original.Name == "AsAsyncEnumerable" ||
                original.Name.EndsWith("Async", System.StringComparison.Ordinal));
    }

    private static Location GetNameLocation(IInvocationOperation invocation)
    {
        return invocation.Syntax switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } =>
                memberAccess.Name.Identifier.GetLocation(),
            InvocationExpressionSyntax { Expression: SimpleNameSyntax name } => name.Identifier.GetLocation(),
            _ => invocation.Syntax.GetLocation()
        };
    }
}
