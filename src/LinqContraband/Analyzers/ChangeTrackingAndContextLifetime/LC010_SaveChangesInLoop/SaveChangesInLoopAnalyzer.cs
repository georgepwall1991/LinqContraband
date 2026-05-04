using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
public class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
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

        // 1. Check method name
        if (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync")
            return;

        // 2. Check containing type (DbContext)
        if (!method.ContainingType.IsDbContext())
            return;

        // 3. Check if inside a loop in the same executable body.
        // A local function or lambda declared inside a loop is not necessarily executed per iteration.
        var loop = invocation.FindEnclosingLoop();
        if (loop != null && invocation.SharesOwningExecutableRoot(loop))
        {
            if (IsSaveInsideCatchGuardedRetryAttempt(invocation, loop))
                return;

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
            return;
        }

        if (IsInsideLocalFunctionCalledFromLoop(invocation))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.Name));
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

    private static bool IsSaveInsideCatchGuardedRetryAttempt(IInvocationOperation invocation, ILoopOperation loop)
    {
        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax)
            return false;

        var tryStatement = FindTryStatementBetweenInvocationAndLoop(invocationSyntax, loop.Syntax);
        return tryStatement != null &&
               tryStatement.Catches.Count > 0 &&
               tryStatement.Block.Span.Contains(invocationSyntax.SpanStart) &&
               HasLoopExitAfterSave(tryStatement.Block, invocationSyntax);
    }

    private static TryStatementSyntax? FindTryStatementBetweenInvocationAndLoop(
        InvocationExpressionSyntax invocationSyntax,
        SyntaxNode loopSyntax)
    {
        foreach (var ancestor in invocationSyntax.Ancestors())
        {
            if (ancestor == loopSyntax)
                return null;

            if (ancestor is TryStatementSyntax tryStatement)
                return tryStatement;
        }

        return null;
    }

    private static bool HasLoopExitAfterSave(BlockSyntax tryBlock, InvocationExpressionSyntax invocationSyntax)
    {
        var containingStatement = invocationSyntax
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent == tryBlock);

        if (containingStatement == null)
            return false;

        var statementIndex = tryBlock.Statements.IndexOf(containingStatement);
        if (statementIndex < 0)
            return false;

        return tryBlock.Statements
            .Skip(statementIndex + 1)
            .Any(statement => statement is BreakStatementSyntax or ReturnStatementSyntax);
    }

    private static ILocalFunctionOperation? FindDirectOwningLocalFunction(IOperation operation)
    {
        var current = operation.Parent;
        while (current != null)
        {
            if (current is ILocalFunctionOperation localFunction)
                return localFunction;

            if (current is IAnonymousFunctionOperation or IMethodBodyOperation)
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
            if (current is IMethodBodyOperation or IAnonymousFunctionOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }
}
