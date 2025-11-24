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
public class SyncBlockerAnalyzer : DiagnosticAnalyzer
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
        Description);

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

        // Case C: LINQ Extension methods on IQueryable
        // These are usually defined in System.Linq.Queryable OR Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        // But "ToList" is NOT on IQueryable, it's on Enumerable. Wait. 
        // EF Core adds "ToListAsync". "ToList" is standard LINQ to Objects.
        // BUT calling .ToList() on an IQueryable triggers DB execution synchronously.
        // So we need to check if the SOURCE is IQueryable.

        ITypeSymbol? receiverType = null;

        if (invocation.Instance != null)
        {
            receiverType = invocation.Instance.Type;
        }
        else if (invocation.Arguments.Length > 0)
        {
            var argVal = invocation.Arguments[0].Value;
            while (argVal is IConversionOperation conv) argVal = conv.Operand;
            receiverType = argVal.Type;
        }

        if (receiverType?.IsIQueryable() == true) return true;

        return false;
    }

    private bool IsInsideAsyncMethod(IOperation operation)
    {
        var parent = operation.Parent;
        while (parent != null)
        {
            if (parent is IMethodBodyOperation)
            {
                // We found the body, need the method symbol.
                // Usually Operation structure for method body:
                // Block -> ...
                // Parent of Block is often null or the method declaration? 
                // Actually, semantic model gives us the enclosing symbol.
                // But we are in Operation context.
            }

            if (parent is ILocalFunctionOperation localFunc) return localFunc.Symbol.IsAsync;

            if (parent is IAnonymousFunctionOperation lambda) return lambda.Symbol.IsAsync;

            parent = parent.Parent;
        }

        // Fallback: Use the SemanticModel to find the enclosing method symbol
        // Note: context.ContainingSymbol works for the analyzer context
        // But we need to walk up if we are in a lambda.
        // Actually, Operation walking is safer for lambdas.
        // If we didn't find a lambda or local function above, we check the owning symbol of the operation context.

        // Wait, context.ContainingSymbol is the method containing the code.
        if (operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart) is IMethodSymbol methodSymbol)
            return methodSymbol.IsAsync;

        return false;
    }
}
