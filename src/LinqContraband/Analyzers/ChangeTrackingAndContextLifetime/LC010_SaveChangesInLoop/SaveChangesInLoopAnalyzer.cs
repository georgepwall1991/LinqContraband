using System.Collections.Immutable;
using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using LinqContraband.Catalog;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Analyzes code for SaveChanges or SaveChangesAsync calls inside loops (N+1 write problem). Diagnostic ID: LC010
/// </summary>
/// <remarks>
/// <para><b>Why this matters:</b> Calling SaveChanges inside a loop results in a separate database transaction and
/// network roundtrip for every iteration, causing severe performance degradation. Instead, entities should be added to
/// the context within the loop and SaveChanges should be called once after the loop completes, enabling batch processing.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC010";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "N+1 Write Problem: SaveChanges inside loop";

    private static readonly LocalizableString MessageFormat =
        "Calling '{0}' inside a loop causes N+1 database writes. Batch changes and call SaveChanges once after the loop.";

    private static readonly LocalizableString Description =
        "Calling SaveChanges or SaveChangesAsync inside a loop results in a separate database transaction and roundtrip for every iteration. This significantly degrades performance. Add all entities to the context and call SaveChanges once after the loop.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC010_SaveChangesInLoop.html");

    private static readonly LocalizableString HelperCallMessageFormat =
        "'{0}' calls '{1}', so this saves once per item and causes N+1 database writes. Batch changes and save once after the loop.";

    /// <summary>
    /// LC010 reported on a per-item call to a helper that saves. It shares LC010's ID and stays out of
    /// <see cref="SupportedDiagnostics"/> (the host accepts any diagnostic whose ID a supported descriptor
    /// declares), so tests and tools keep resolving LC010 to a single descriptor.
    /// </summary>
    public static readonly DiagnosticDescriptor HelperCallRule = new(
        DiagnosticId,
        Title,
        HelperCallMessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        true,
        Description,
        helpLinkUri: RuleCatalog.DocumentationSiteUri + "LC010_SaveChangesInLoop.html");

    /// <summary>Set on a diagnostic reported on a helper call, which the code fix does not move.</summary>
    internal const string HelperCallProperty = "HelperCall";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var saveHelpers = new NPlusOneLooperHelperCache(
                compilationContext.Compilation,
                NPlusOneLooperAnalysis.MaxHelperDepth,
                MatchSaveInHelper,
                skipMemoizedHelpers: false);
            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, saveHelpers),
                OperationKind.Invocation);
            compilationContext.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
        });
    }

    private void AnalyzeInvocation(OperationAnalysisContext context, NPlusOneLooperHelperCache saveHelpers)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Anything but DbContext.SaveChanges[Async] may be a helper that saves.
        if ((method.Name != "SaveChanges" && method.Name != "SaveChangesAsync") ||
            !method.ContainingType.IsDbContext())
        {
            AnalyzeHelperCall(context, invocation, saveHelpers);
            return;
        }

        // 3. Check if inside a loop in the same executable body.
        // A local function or lambda declared inside a loop is not necessarily executed per iteration.
        var loop = FindSaveExecutionLoop(invocation);

        // One save per batch (a Chunk(...) loop, or a loop that pages, drains or polls the database) is how large
        // jobs keep the change tracker small. It still reports when an outer per-item loop repeats the batches.
        // A loop whose body runs at most once (a LanguageExt Option, or a body that always returns) saves once, and
        // so does a save on the path that leaves the loop (`SaveChanges(); return;`).
        while (loop != null &&
               (loop.RunsAtMostOnce() ||
                invocation.LeavesLoopAfter(loop) ||
                NPlusOneLooperAnalysis.IsBatchLoop(loop, context.CancellationToken)))
            loop = FindEnclosingLoopOutsideSameRoot(invocation, loop);

        if (loop != null)
        {
            if (IsSaveReceiverFreshContextDeclaredInsideLoopBody(invocation, loop))
                return;

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
            return;
        }

        if (IsInsideLocalFunctionCalledFromLoop(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
            return;
        }

        if (IsInsideDelegateCalledFromLoop(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
            return;
        }

        if (IsInsidePerElementLambda(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
        }
    }

    /// <summary>
    /// A save inside a lambda that an in-memory operator runs once per element (<c>items.ForEach(...)</c>,
    /// <c>items.Select(...)</c>, <c>Parallel.ForEach(...)</c>) is a save per item, unless the context it saves
    /// belongs to the element: a local the lambda declares, or the lambda's own parameter.
    /// </summary>
    private static bool IsInsidePerElementLambda(IInvocationOperation invocation)
    {
        if (NPlusOneLooperAnalysis.FindPerElementOperator(invocation, out var lambda) == null ||
            lambda == null ||
            invocation.Instance == null)
        {
            return false;
        }

        foreach (var operation in invocation.Instance.DescendantsAndSelf())
        {
            switch (operation)
            {
                case IParameterReferenceOperation parameterReference
                    when SymbolEqualityComparer.Default.Equals(parameterReference.Parameter.ContainingSymbol, lambda.Symbol):
                    return false;
                case ILocalReferenceOperation localReference
                    when IsDeclaredInside(localReference.Local, lambda):
                    return false;
            }
        }

        return true;
    }

    private static bool IsDeclaredInside(ILocalSymbol local, IAnonymousFunctionOperation lambda)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == lambda.Syntax.SyntaxTree && lambda.Syntax.Span.Contains(reference.Span))
                return true;
        }

        return false;
    }

    private void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        var methodReference = (IMethodReferenceOperation)context.Operation;
        var method = methodReference.Method;

        if (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync")
            return;

        if (!method.ContainingType.IsDbContext())
            return;

        if (IsSaveMethodReferenceAssignedToDelegateCalledFromLoop(methodReference))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, methodReference.Syntax.GetLocation(), method.Name));
        }
    }

    private static bool IsInsideLocalFunctionCalledFromLoop(IInvocationOperation invocation)
    {
        var localFunction = FindDirectOwningLocalFunction(invocation);
        if (localFunction == null)
            return false;

        var containingRoot = FindContainingExecutableRoot(localFunction);
        if (containingRoot == null)
            return false;

        foreach (var descendant in containingRoot.Descendants())
        {
            if (descendant is not IInvocationOperation call ||
                !SymbolEqualityComparer.Default.Equals(call.TargetMethod, localFunction.Symbol))
            {
                continue;
            }

            var loop = call.FindEnclosingLoop();
            if (loop != null && call.SharesOwningExecutableRoot(loop))
                return true;
        }

        return false;
    }

    private static ILocalFunctionOperation? FindDirectOwningLocalFunction(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ILocalFunctionOperation localFunction)
                return localFunction;

            if (current is IAnonymousFunctionOperation or IMethodBodyBaseOperation)
                return null;

            current = current.Parent;
        }

        return null;
    }

    private static IOperation? FindContainingExecutableRoot(ILocalFunctionOperation localFunction)
    {
        var current = localFunction.Parent;
        while (current != null)
        {
            if (current is IMethodBodyBaseOperation or IAnonymousFunctionOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }
}
