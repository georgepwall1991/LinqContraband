using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Provides code fixes for LC032. Rewrites a tracked bulk-update foreach loop into a single
/// set-based <c>ExecuteUpdate</c>/<c>ExecuteUpdateAsync</c> call. The trailing
/// <c>SaveChanges</c> is left in place (it becomes a no-op for the converted rows but still
/// flushes any unrelated pending changes), and a warning comment flags the behaviour change.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExecuteUpdateForBulkUpdatesFixer))]
[Shared]
public sealed class ExecuteUpdateForBulkUpdatesFixer : CodeFixProvider
{
    private const string WarningComment =
        "// Warning: ExecuteUpdate runs immediately and bypasses change tracking and entity callbacks.";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExecuteUpdateForBulkUpdatesAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var forEach = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<ForEachStatementSyntax>()
            .FirstOrDefault();

        if (forEach is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        if (!TryBuildPlan(forEach, semanticModel, out var plan))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteUpdate()",
                c => ApplyFixAsync(context.Document, forEach, plan, c),
                "UseExecuteUpdate"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        ForEachStatementSyntax forEach,
        RewritePlan plan,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // The original loop can compile on DbContext/DbSet instance members plus System.Linq;
        // the generated ExecuteUpdate(...) extension needs its defining namespace in scope.
        if (plan.ImportNamespace is { Length: > 0 } importNamespace)
            editor.EnsureUsing(importNamespace);

        var builder = new StringBuilder();
        if (plan.IsAsync)
            builder.Append("await ");

        builder.Append(plan.ReceiverText);
        builder.Append(plan.IsAsync ? ".ExecuteUpdateAsync(setters => setters" : ".ExecuteUpdate(setters => setters");

        foreach (var (left, right) in plan.Setters)
        {
            builder.Append(".SetProperty(");
            builder.Append(plan.IterationName);
            builder.Append(" => ");
            builder.Append(left);
            builder.Append(", ");
            builder.Append(plan.IterationName);
            builder.Append(" => ");
            builder.Append(right);
            builder.Append(')');
        }

        // Carry the cancellation token from the awaited SaveChangesAsync onto ExecuteUpdateAsync
        // (which is now the actual database call) so cancellation is not silently lost.
        if (plan.IsAsync && plan.CancellationTokenText is { Length: > 0 } token)
        {
            builder.Append(", ");
            builder.Append(token);
        }

        builder.Append(");");

        var warning = SyntaxFactory.Comment(WarningComment);
        var newStatement = SyntaxFactory.ParseStatement(builder.ToString())
            .WithLeadingTrivia(forEach.GetLeadingTrivia().Add(warning).Add(SyntaxFactory.ElasticLineFeed))
            .WithTrailingTrivia(forEach.GetTrailingTrivia());

        editor.ReplaceNode(forEach, newStatement);

        return editor.GetChangedDocument();
    }

    private static bool TryBuildPlan(ForEachStatementSyntax forEach, SemanticModel semanticModel, out RewritePlan plan)
    {
        plan = null!;

        // A local-variable source (pre-materialized list or query local) would orphan the
        // local and/or produce a type-invalid receiver, so decline it in v1. The first check is
        // a fast path; the post-strip check is the load-bearing one (a bare identifier is never
        // stripped, but the receiver under a materializer could itself be an identifier).
        if (forEach.Expression is IdentifierNameSyntax)
            return false;

        var receiver = StripCollectionMaterializer(forEach.Expression);
        if (receiver is IdentifierNameSyntax)
            return false;

        var receiverType = semanticModel.GetTypeInfo(receiver).Type;
        if (receiverType is null || (!receiverType.IsIQueryable() && !receiverType.IsDbSet()))
            return false;

        if (!TryGetSetters(forEach, forEach.Identifier.Text, out var setters))
            return false;

        if (!TryClassifyTrailingSaveChanges(forEach, semanticModel, out var trailingIsAwaited, out var cancellationTokenText))
            return false;

        var mode = DetermineRewriteMode(forEach, semanticModel, trailingIsAwaited);
        if (mode == RewriteMode.None)
            return false;

        // A token can only be preserved on an awaited ExecuteUpdateAsync overload that accepts
        // one. If the trailing SaveChanges carried a token but the rewrite would be synchronous
        // (e.g. an unawaited SaveChangesAsync(token)) or no token-accepting overload exists,
        // decline rather than silently drop the developer's cancellation intent.
        if (cancellationTokenText is not null &&
            (mode != RewriteMode.Async || !HasExecuteUpdateAsyncTokenOverload(semanticModel.Compilation)))
        {
            return false;
        }

        plan = new RewritePlan(
            receiver.WithoutTrivia().ToString(),
            setters,
            forEach.Identifier.Text,
            mode == RewriteMode.Async,
            ResolveExecuteUpdateNamespace(semanticModel.Compilation, mode == RewriteMode.Async),
            mode == RewriteMode.Async ? cancellationTokenText : null);
        return true;
    }

    private static ExpressionSyntax StripCollectionMaterializer(ExpressionSyntax expression)
    {
        // The analyzer's query walker unwraps `await`, so an inline async materializer such as
        // `await db.Users.Where(...).ToListAsync()` reaches the fixer; unwrap it before stripping.
        if (expression is AwaitExpressionSyntax awaitExpression)
            expression = awaitExpression.Expression;

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.ArgumentList.Arguments.Count == 0 &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            IsCollectionMaterializer(memberAccess.Name.Identifier.Text))
        {
            return memberAccess.Expression;
        }

        return expression;
    }

    private static bool IsCollectionMaterializer(string methodName) =>
        methodName is "ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync";

    private static bool TryGetSetters(ForEachStatementSyntax forEach, string iterationName, out ImmutableArray<(string Left, string Right)> setters)
    {
        setters = ImmutableArray<(string, string)>.Empty;

        var statements = forEach.Statement is BlockSyntax block
            ? (IReadOnlyList<StatementSyntax>)block.Statements
            : new[] { forEach.Statement };

        if (statements.Count == 0)
            return false;

        var assignments = new List<(string Left, string PropertyName, AssignmentExpressionSyntax Node)>();

        foreach (var statement in statements)
        {
            if (statement is not ExpressionStatementSyntax expressionStatement)
                return false;

            if (expressionStatement.Expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.Left is not MemberAccessExpressionSyntax target)
            {
                return false;
            }

            assignments.Add((assignment.Left.WithoutTrivia().ToString(), target.Name.Identifier.Text, assignment));
        }

        // ExecuteUpdate evaluates every value expression against the ORIGINAL row, so the
        // set-based rewrite only matches the loop's sequential semantics when no value reads a
        // property written earlier in the same iteration (e.g. a second `user.Name = user.Name
        // + "!"` would read the pre-loop value, not the just-assigned one). Decline that shape.
        var writtenSoFar = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, propertyName, node) in assignments)
        {
            if (ReadsAnyProperty(node.Right, iterationName, writtenSoFar))
                return false;

            writtenSoFar.Add(propertyName);
        }

        // Collapse duplicate targets to the last write (their values are independent of earlier
        // writes by the check above), preserving first-seen order.
        var order = new List<string>();
        var lastValue = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (left, _, node) in assignments)
        {
            if (!lastValue.ContainsKey(left))
                order.Add(left);

            lastValue[left] = node.Right.WithoutTrivia().ToString();
        }

        setters = order.Select(left => (left, lastValue[left])).ToImmutableArray();
        return setters.Length > 0;
    }

    private static bool ReadsAnyProperty(ExpressionSyntax expression, string iterationName, HashSet<string> propertyNames)
    {
        // Matching `iterationVar.Prop` is sufficient here: the analyzer has already restricted every
        // RHS to direct scalar members of the iteration variable (see the assignment analysis), so
        // deeper shapes like `iterationVar.Nav.Prop` never reach the fixer.
        if (propertyNames.Count == 0)
            return false;

        foreach (var member in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (member.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == iterationName &&
                propertyNames.Contains(member.Name.Identifier.Text))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveExecuteUpdateNamespace(Compilation compilation, bool async)
    {
        var methodName = async ? "ExecuteUpdateAsync" : "ExecuteUpdate";

        foreach (var typeName in new[]
                 {
                     "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
                     "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                 })
        {
            var method = compilation.GetTypeByMetadataName(typeName)?
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(IsExecuteUpdateLikeMethod);

            if (method is not null)
                return method.ContainingNamespace?.ToString();
        }

        return compilation.GetSymbolsWithName(methodName, SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(IsExecuteUpdateLikeMethod)?
            .ContainingNamespace?.ToString();
    }

    private static bool IsExecuteUpdateLikeMethod(IMethodSymbol method) =>
        method.IsExtensionMethod && method.Parameters.Length > 0 && method.Parameters[0].Type.IsIQueryable();

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

    private static bool HasExecuteUpdateAsyncTokenOverload(Compilation compilation)
    {
        bool AcceptsTrailingToken(IMethodSymbol method)
        {
            if (!IsExecuteUpdateAsyncLikeMethod(method))
                return false;

            var last = method.Parameters[method.Parameters.Length - 1].Type;
            return last is { Name: "CancellationToken" } &&
                   last.ContainingNamespace?.ToString() == "System.Threading";
        }

        if (compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")?
                .GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(AcceptsTrailingToken) == true ||
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")?
                .GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(AcceptsTrailingToken) == true)
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(AcceptsTrailingToken);
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

    private static bool HasExecuteUpdateAsyncSupport(Compilation compilation)
    {
        if (HasExecuteUpdateAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")) ||
            HasExecuteUpdateAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(IsExecuteUpdateAsyncLikeMethod);
    }

    private static bool HasExecuteUpdateAsyncMethod(INamedTypeSymbol? type)
    {
        return type?.GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any(IsExecuteUpdateAsyncLikeMethod) == true;
    }

    private static bool IsExecuteUpdateAsyncLikeMethod(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod || method.Parameters.Length == 0)
            return false;

        if (!IsEntityFrameworkCoreNamespace(method.ContainingNamespace))
            return false;

        return method.Parameters[0].Type.IsIQueryable();
    }

    private static bool IsEntityFrameworkCoreNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) == true;
    }

    private sealed class RewritePlan
    {
        public RewritePlan(string receiverText, ImmutableArray<(string Left, string Right)> setters, string iterationName, bool isAsync, string? importNamespace, string? cancellationTokenText)
        {
            ReceiverText = receiverText;
            Setters = setters;
            IterationName = iterationName;
            IsAsync = isAsync;
            ImportNamespace = importNamespace;
            CancellationTokenText = cancellationTokenText;
        }

        public string ReceiverText { get; }

        public ImmutableArray<(string Left, string Right)> Setters { get; }

        public string IterationName { get; }

        public bool IsAsync { get; }

        public string? ImportNamespace { get; }

        public string? CancellationTokenText { get; }
    }
}
