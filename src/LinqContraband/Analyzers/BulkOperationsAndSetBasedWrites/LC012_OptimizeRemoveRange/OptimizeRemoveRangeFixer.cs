using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

/// <summary>
/// Provides code fixes for LC012. Replaces RemoveRange with ExecuteDelete.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OptimizeRemoveRangeFixer))]
[Shared]
public sealed class OptimizeRemoveRangeFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OptimizeRemoveRangeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        if (!await CanSafelyRewriteAsync(context.Document, invocation, context.CancellationToken).ConfigureAwait(false))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteDelete()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseExecuteDelete"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // RemoveRange(query) -> query.ExecuteDelete() / await query.ExecuteDeleteAsync()
        if (invocation.ArgumentList.Arguments.Count == 0)
            return editor.GetChangedDocument();

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var mode = semanticModel == null ? RewriteMode.Sync : DetermineRewriteMode(invocation, semanticModel);

        // CanSafelyRewriteAsync already declined registration for RewriteMode.None, so a
        // safe sync/async target always exists by the time the fix is applied.
        if (mode == RewriteMode.None)
            return document;

        var queryExpression = invocation.ArgumentList.Arguments[0].Expression;

        var executeDeleteName = SyntaxFactory.IdentifierName(mode == RewriteMode.Async ? "ExecuteDeleteAsync" : "ExecuteDelete");
        var memberAccess = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, queryExpression, executeDeleteName);
        ExpressionSyntax replacement = SyntaxFactory.InvocationExpression(memberAccess);

        // Inside an async context the synchronous ExecuteDelete() rewrite would inject a
        // blocking, sync-over-async database call (the smell LC008 flags), so await the
        // async overload instead.
        if (mode == RewriteMode.Async)
            replacement = SyntaxFactory.AwaitExpression(replacement);

        // Add warning comment
        var warningComment = SyntaxFactory.Comment("// Warning: ExecuteDelete bypasses change tracking and cascades.");
        var replacementWithComment = replacement.WithLeadingTrivia(invocation.GetLeadingTrivia().Add(warningComment).Add(SyntaxFactory.ElasticLineFeed));

        editor.ReplaceNode(invocation, replacementWithComment);

        return editor.GetChangedDocument();
    }

    private static async Task<bool> CanSafelyRewriteAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return false;

        var sourceType = semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type;
        if (!sourceType.IsIQueryable() && !sourceType.IsDbSet())
            return false;

        if (HasSubsequentSaveChangesInvocation(invocation, semanticModel, cancellationToken))
            return false;

        // Decline rather than emit an unsafe sync-over-async ExecuteDelete() when the call
        // sits in an async context but no awaitable ExecuteDeleteAsync overload is available.
        return DetermineRewriteMode(invocation, semanticModel) != RewriteMode.None;
    }

    private enum RewriteMode
    {
        None,
        Sync,
        Async
    }

    private static RewriteMode DetermineRewriteMode(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (!IsAsyncContext(invocation))
            return RewriteMode.Sync;

        return HasExecuteDeleteAsyncSupport(semanticModel.Compilation)
            ? RewriteMode.Async
            : RewriteMode.None;
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

    private static bool HasExecuteDeleteAsyncSupport(Compilation compilation)
    {
        if (HasExecuteDeleteAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")) ||
            HasExecuteDeleteAsyncMethod(compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteDeleteAsync", SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Any(IsExecuteDeleteAsyncLikeMethod);
    }

    private static bool HasExecuteDeleteAsyncMethod(INamedTypeSymbol? type)
    {
        return type?.GetMembers("ExecuteDeleteAsync").OfType<IMethodSymbol>().Any(IsExecuteDeleteAsyncLikeMethod) == true;
    }

    private static bool IsExecuteDeleteAsyncLikeMethod(IMethodSymbol method)
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

    private static bool HasSubsequentSaveChangesInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation removeRangeOperation)
            return HasSubsequentSaveChangesInvocationBySyntax(invocation, semanticModel, cancellationToken);

        var executableRoot = removeRangeOperation.FindOwningExecutableRoot();
        if (executableRoot == null)
            return HasSubsequentSaveChangesInvocationBySyntax(invocation, semanticModel, cancellationToken);

        var removeRangeReceiver = GetRemoveRangeContextReceiver(removeRangeOperation);

        foreach (var candidate in executableRoot.Descendants().OfType<IInvocationOperation>())
        {
            if (candidate.Syntax.SpanStart <= invocation.SpanStart ||
                !IsSaveChangesMethod(candidate.TargetMethod))
            {
                continue;
            }

            if (AreMutuallyExclusiveBranches(invocation, candidate.Syntax))
                continue;

            if (removeRangeReceiver != null &&
                TryResolveFreshContextLocal(removeRangeReceiver, executableRoot, cancellationToken, out var removeLocal) &&
                TryResolveFreshContextLocal(candidate.Instance, executableRoot, cancellationToken, out var saveLocal) &&
                !SymbolEqualityComparer.Default.Equals(removeLocal, saveLocal) &&
                TryResolveQuerySourceFreshContextLocal(removeRangeOperation.Arguments[0].Value, executableRoot, cancellationToken, out var queryLocal) &&
                SymbolEqualityComparer.Default.Equals(removeLocal, queryLocal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasSubsequentSaveChangesInvocationBySyntax(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var executableRoot = FindExecutableSyntaxRoot(invocation);
        if (executableRoot == null)
            return false;

        foreach (var subsequentInvocation in executableRoot
                     .DescendantNodes()
                     .OfType<InvocationExpressionSyntax>()
                     .Where(node => node.SpanStart > invocation.SpanStart))
        {
            if (semanticModel.GetSymbolInfo(subsequentInvocation, cancellationToken).Symbol is IMethodSymbol method &&
                IsSaveChangesMethod(method))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? FindExecutableSyntaxRoot(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or
                AccessorDeclarationSyntax or BaseMethodDeclarationSyntax)
            {
                return ancestor;
            }
        }

        return null;
    }

    private static IOperation? GetRemoveRangeContextReceiver(IInvocationOperation invocation)
    {
        var receiver = invocation.GetInvocationReceiver();
        if (receiver == null)
            return null;

        if (invocation.TargetMethod.ContainingType.IsDbContext())
            return receiver;

        return receiver is IMemberReferenceOperation memberReference ? memberReference.Instance : null;
    }

    private static bool TryResolveFreshContextLocal(
        IOperation? receiver,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out ILocalSymbol? creationLocal)
    {
        creationLocal = null;
        var current = receiver?.UnwrapConversions();

        for (var depth = 0; depth < 16; depth++)
        {
            if (current is not ILocalReferenceOperation localReference)
                return false;

            var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);
            if (assignments.Count != 1)
                return false;

            var value = assignments[0].Value.UnwrapConversions();
            if (value is IObjectCreationOperation)
            {
                creationLocal = localReference.Local;
                return true;
            }

            current = value;
        }

        return false;
    }

    private static bool TryResolveQuerySourceFreshContextLocal(
        IOperation? querySource,
        IOperation executableRoot,
        CancellationToken cancellationToken,
        out ILocalSymbol? creationLocal)
    {
        creationLocal = null;
        var current = querySource?.UnwrapConversions();

        for (var depth = 0; depth < 16; depth++)
        {
            switch (current)
            {
                case ILocalReferenceOperation localReference:
                {
                    var assignments = LocalAssignmentCache.GetAssignments(executableRoot, localReference.Local, cancellationToken);
                    if (assignments.Count != 1)
                        return false;

                    current = assignments[0].Value.UnwrapConversions();
                    continue;
                }

                case IInvocationOperation invocation
                    when TryGetTransparentQueryInvocationSource(invocation, out var invocationSource):
                    current = invocationSource.UnwrapConversions();
                    continue;

                case IInvocationOperation:
                    return false;

                case IMemberReferenceOperation memberReference:
                    return TryResolveFreshContextLocal(memberReference.Instance, executableRoot, cancellationToken, out creationLocal);

                default:
                    return TryResolveFreshContextLocal(current, executableRoot, cancellationToken, out creationLocal);
            }
        }

        return false;
    }

    private static bool TryGetTransparentQueryInvocationSource(
        IInvocationOperation invocation,
        out IOperation source)
    {
        source = null!;
        var method = invocation.TargetMethod;

        if (method.Name == "Set" && method.ContainingType.IsDbContext() && invocation.Instance != null)
        {
            source = invocation.Instance;
            return true;
        }

        if (!method.IsExtensionMethod || invocation.Arguments.Length == 0)
            return false;

        if (!IsSingleSourceTransparentQueryMethod(method))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        if (namespaceName != "System.Linq" &&
            namespaceName != "Microsoft.EntityFrameworkCore" &&
            namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) != true)
        {
            return false;
        }

        var candidate = invocation.Arguments[0].Value.UnwrapConversions();
        if (!candidate.Type.IsIQueryable() && !candidate.Type.IsDbSet())
            return false;

        source = candidate;
        return true;
    }

    private static bool IsSingleSourceTransparentQueryMethod(IMethodSymbol method)
    {
        return method.Name is
            "Where" or
            "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or
            "Skip" or "Take" or
            "Distinct" or "Reverse" or
            "AsQueryable" or
            "AsNoTracking" or "AsNoTrackingWithIdentityResolution" or "AsTracking" or
            "AsSplitQuery" or "AsSingleQuery" or
            "TagWith" or "IgnoreQueryFilters" or
            "Include" or "ThenInclude";
    }

    private static bool AreMutuallyExclusiveBranches(SyntaxNode left, SyntaxNode right)
    {
        foreach (var ifStatement in left.AncestorsAndSelf().OfType<IfStatementSyntax>())
        {
            if (!ifStatement.Span.Contains(right.SpanStart))
                continue;

            var leftBranch = GetContainingIfBranch(ifStatement, left);
            var rightBranch = GetContainingIfBranch(ifStatement, right);

            if (leftBranch != null && rightBranch != null && leftBranch != rightBranch)
                return true;
        }

        foreach (var switchStatement in left.AncestorsAndSelf().OfType<SwitchStatementSyntax>())
        {
            if (!switchStatement.Span.Contains(right.SpanStart))
                continue;

            if (switchStatement.DescendantNodes().Any(node =>
                    node.IsKind(SyntaxKind.GotoCaseStatement) || node.IsKind(SyntaxKind.GotoDefaultStatement)))
            {
                continue;
            }

            var leftSection = GetContainingSwitchSection(switchStatement, left);
            var rightSection = GetContainingSwitchSection(switchStatement, right);

            if (leftSection != null && rightSection != null && leftSection != rightSection)
                return true;
        }

        return false;
    }

    private static SyntaxNode? GetContainingIfBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        if (ifStatement.Statement.Span.Contains(node.Span))
            return ifStatement.Statement;

        var elseClause = ifStatement.Else;
        return elseClause != null && elseClause.Span.Contains(node.Span) ? elseClause : null;
    }

    private static SwitchSectionSyntax? GetContainingSwitchSection(SwitchStatementSyntax switchStatement, SyntaxNode node)
    {
        foreach (var section in switchStatement.Sections)
        {
            if (section.Span.Contains(node.Span))
                return section;
        }

        return null;
    }

    private static bool IsSaveChangesMethod(IMethodSymbol method)
    {
        return (method.Name is "SaveChanges" or "SaveChangesAsync") &&
               method.ContainingType.IsDbContext();
    }
}
