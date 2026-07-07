using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private enum RewriteMode
    {
        None,
        Sync,
        Async
    }

    private static RewriteMode DetermineRewriteMode(ForEachStatementSyntax forEach, SemanticModel semanticModel, bool trailingIsAwaited)
    {
        // Async when the trailing SaveChanges is awaited (covers top-level programs, which have
        // no async function ancestor) or the nearest enclosing function is async.
        if (!trailingIsAwaited && !IsAsyncContext(forEach))
            return RewriteMode.Sync;

        return HasExecuteUpdateAsyncSupport(semanticModel.Compilation)
            ? RewriteMode.Async
            : RewriteMode.None;
    }

    private static bool TryClassifyTrailingSaveChanges(
        ForEachStatementSyntax forEach,
        SemanticModel semanticModel,
        out bool isAwaited,
        out string? cancellationTokenText)
    {
        isAwaited = false;
        cancellationTokenText = null;

        // The analyzer proved the next statement is SaveChanges on the same context. Only offer
        // the fix when that result is discarded: a bare expression statement. `return
        // db.SaveChanges();`, `var n = db.SaveChanges();`, etc. observe the affected-row count,
        // which the rewrite would change (the leftover SaveChanges then returns 0), so decline.
        if (FindFollowingStatement(forEach) is not ExpressionStatementSyntax saveStatement)
            return false;

        var expression = saveStatement.Expression;
        if (expression is AwaitExpressionSyntax await)
        {
            isAwaited = true;
            expression = await.Expression;
        }

        // Require a direct SaveChanges/SaveChangesAsync invocation. This declines wrapper shapes
        // such as `_ = db.SaveChangesAsync(token);` or `db.SaveChangesAsync(token).Wait();`, where
        // a token would otherwise be lost in a synchronous rewrite.
        if (expression is not InvocationExpressionSyntax saveInvocation ||
            saveInvocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "SaveChanges" or "SaveChangesAsync" })
        {
            return false;
        }

        // Capture a cancellation token argument (e.g. SaveChangesAsync(ct)) so it can be carried
        // onto the ExecuteUpdateAsync call that replaces it.
        cancellationTokenText = FindCancellationTokenArgument(saveInvocation, semanticModel);
        return true;
    }

    private static string? FindCancellationTokenArgument(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var type = semanticModel.GetTypeInfo(argument.Expression).Type;
            if (type is { Name: "CancellationToken" } &&
                type.ContainingNamespace?.ToString() == "System.Threading")
            {
                return argument.Expression.WithoutTrivia().ToString();
            }
        }

        return null;
    }

    private static StatementSyntax? FindFollowingStatement(ForEachStatementSyntax forEach)
    {
        switch (forEach.Parent)
        {
            case BlockSyntax block:
            {
                var index = block.Statements.IndexOf(forEach);
                return index >= 0 && index + 1 < block.Statements.Count
                    ? block.Statements[index + 1]
                    : null;
            }
            case GlobalStatementSyntax global when global.Parent is CompilationUnitSyntax unit:
            {
                var members = unit.Members;
                var index = members.IndexOf(global);
                return index >= 0 && index + 1 < members.Count && members[index + 1] is GlobalStatementSyntax next
                    ? next.Statement
                    : null;
            }
            default:
                return null;
        }
    }

    private static bool IsAsyncContext(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case AccessorDeclarationSyntax:
                    // Property/event accessors cannot be async.
                    return false;
                case BaseMethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }
        }

        return false;
    }
}
