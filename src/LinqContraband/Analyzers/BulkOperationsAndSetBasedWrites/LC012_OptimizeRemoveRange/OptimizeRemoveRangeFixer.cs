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

namespace LinqContraband.Analyzers.LC012_OptimizeRemoveRange;

/// <summary>
/// Provides code fixes for LC012. Replaces RemoveRange with ExecuteDelete.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OptimizeRemoveRangeFixer))]
[Shared]
public class OptimizeRemoveRangeFixer : CodeFixProvider
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

    private static bool IsSaveChangesMethod(IMethodSymbol method)
    {
        return (method.Name is "SaveChanges" or "SaveChangesAsync") &&
               method.ContainingType.IsDbContext();
    }
}
