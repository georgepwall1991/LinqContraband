using System.Collections.Immutable;
using LinqContraband.Constants;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC008_SyncBlocker;

/// <summary>
/// Analyzes synchronous Entity Framework operations called within async methods, causing thread blocking. Diagnostic ID: LC008
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Calling synchronous database methods (ToList, SaveChanges, Find) inside async methods
/// blocks threads while waiting for I/O, preventing them from handling other requests. This causes thread pool starvation
/// in web applications, drastically reducing throughput and potentially causing request timeouts under load. Always use the
/// async alternatives (ToListAsync, SaveChangesAsync, FindAsync) with await to release threads back to the pool while waiting
/// for database operations, allowing the server to handle more concurrent requests with the same resources.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class SyncBlockerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC008";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Sync-over-Async: Synchronous EF Core method in Async context";

    private static readonly LocalizableString MessageFormat =
        "Calling synchronous '{0}' inside an async method blocks the thread. Use '{1}' and await it.";

    private static readonly LocalizableString Description =
        "Avoid synchronous database blocking calls inside async methods. This leads to thread pool starvation and reduced throughput.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: "https://github.com/georgepwall1991/LinqContraband/blob/master/docs/LC008_SyncBlocker.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // 1. Is it a banned sync method?
        if (!SyncAsyncMappings.SyncToAsyncMap.TryGetValue(method.Name, out var asyncMethodName)) return;

        // 2. Is it an EF Core related method?
        if (!IsEfCoreMethod(method, invocation)) return;

        if (IsInsideQueryableExpressionLambda(invocation)) return;

        // 3. Is the containing method Async?
        if (!IsInsideAsyncMethod(context.Operation)) return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name, asyncMethodName));
    }

    private bool IsEfCoreMethod(IMethodSymbol method, IInvocationOperation invocation)
    {
        // Case A: DbContext.SaveChanges
        if (method.Name == "SaveChanges")
            // Check if instance is DbContext
            return method.ContainingType.IsDbContext();

        // Case B: DbSet.Find
        if (method.Name == "Find") return method.ContainingType.IsDbSet();

        // Case C: LINQ extension methods over IQueryable execute the database query synchronously.

        var receiverType = invocation.GetInvocationReceiverType();

        return receiverType?.IsIQueryable() == true;
    }

    private static bool IsInsideQueryableExpressionLambda(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is IAnonymousFunctionOperation lambda &&
                IsLambdaArgumentToQueryableInvocation(lambda))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsLambdaArgumentToQueryableInvocation(IAnonymousFunctionOperation lambda)
    {
        var current = lambda.Parent;
        while (current != null)
        {
            if (current is IArgumentOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current is IConversionOperation or IDelegateCreationOperation)
            {
                current = current.Parent;
                continue;
            }

            if (current is not IInvocationOperation invocation)
                return false;

            if (invocation.TargetMethod.ContainingType.Name == "Queryable" &&
                invocation.TargetMethod.ContainingType.ContainingNamespace?.ToString() == "System.Linq")
            {
                return true;
            }

            return invocation.GetInvocationReceiverType()?.IsIQueryable() == true;
        }

        return false;
    }
}
