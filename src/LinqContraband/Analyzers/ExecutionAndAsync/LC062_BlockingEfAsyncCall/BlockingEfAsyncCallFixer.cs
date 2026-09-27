using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Analyzers.LC008_SyncBlocker;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC062_BlockingEfAsyncCall;

/// <summary>
/// Provides code fixes for LC062. In an async method, lambda or local function the blocking access becomes
/// <c>await</c> on the task. Elsewhere a direct call becomes its synchronous EF Core counterpart
/// (<c>ToListAsync().Result</c> to <c>ToList()</c>), dropping a <c>CancellationToken</c> argument the synchronous
/// method does not take.
/// </summary>
/// <remarks>
/// No fix is offered where neither rewrite is provably safe: a non-async lambda inside an async method (the
/// synchronous call would trade LC062 for LC008), a task stored in a local outside async code, a <c>Wait</c> with a
/// timeout, and code inside a <c>try</c> that catches <c>AggregateException</c>, which <c>.Result</c> and
/// <c>.Wait()</c> throw and the rewrites do not. The fixer compiles the rewritten document and only offers a
/// rewrite that adds no errors and keeps the value's type.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingEfAsyncCallFixer))]
[Shared]
public sealed class BlockingEfAsyncCallFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(BlockingEfAsyncCallAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var site = FindSite(root, diagnostic.Location.SourceSpan);
            if (site == null || !TryGetTaskExpression(site, out var taskExpression, out var producesValue)) continue;
            if (IsInsideAggregateExceptionCatch(site, semanticModel, cancellationToken)) continue;

            var siteOperation = semanticModel.GetOperation(site, cancellationToken);
            if (siteOperation == null) continue;

            var originalType = producesValue ? semanticModel.GetTypeInfo(site, cancellationToken).Type : null;

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);

            // In async code: await the task.
            if (!SyncBlockerFixer.IsInvalidAwaitContext(site) &&
                !SyncBlockerFixer.WouldStrandRefStructLocal(site, semanticModel, cancellationToken))
            {
                var awaitMarker = new SyntaxAnnotation();
                var awaitedDraft = await CreateAwaitFixAsync(document, site, taskExpression, awaitMarker, cancellationToken).ConfigureAwait(false);
                if (await IsAcceptableAsync(awaitedDraft, awaitMarker, errorsBefore.Value, originalType, cancellationToken).ConfigureAwait(false))
                {
                    var awaited = await WithoutMarkerAsync(awaitedDraft, awaitMarker, cancellationToken).ConfigureAwait(false);
                    context.RegisterCodeFix(
                        CodeAction.Create("Await the task instead of blocking", _ => Task.FromResult(awaited), nameof(BlockingEfAsyncCallFixer)),
                        diagnostic);
                    continue;
                }
            }

            // Outside async code: call the synchronous EF Core method. Inside it (a sync lambda in an async
            // method) that call would be LC008's finding, so no fix is offered.
            if (SyncBlockerAnalyzer.IsInsideAsyncMethod(siteOperation) ||
                (!producesValue && site.Parent is not ExpressionStatementSyntax) ||
                !TryGetDirectEfInvocation(semanticModel, taskExpression, cancellationToken, out var efInvocation, out var efSyntax))
            {
                continue;
            }

            var asyncName = efInvocation.TargetMethod.Name;
            var syncName = asyncName.Substring(0, asyncName.Length - "Async".Length);
            var syncMarker = new SyntaxAnnotation();
            var synchronousDraft = await CreateSyncFixAsync(document, site, efInvocation, efSyntax, syncName, syncMarker, cancellationToken)
                .ConfigureAwait(false);
            if (!await IsAcceptableAsync(synchronousDraft, syncMarker, errorsBefore.Value, originalType, cancellationToken).ConfigureAwait(false) ||
                !await BindsToEfOrLinqAsync(synchronousDraft, syncMarker, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var synchronous = await WithoutMarkerAsync(synchronousDraft, syncMarker, cancellationToken).ConfigureAwait(false);

            context.RegisterCodeFix(
                CodeAction.Create($"Call {syncName} instead of blocking", _ => Task.FromResult(synchronous), nameof(BlockingEfAsyncCallFixer)),
                diagnostic);
        }
    }

    private static ExpressionSyntax? FindSite(SyntaxNode root, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        return node.AncestorsAndSelf().OfType<ExpressionSyntax>().FirstOrDefault(expression => expression.Span == span);
    }

    /// <summary>
    /// The task a blocking site waits on: <c>X</c> in <c>X.Result</c>, <c>X.Wait()</c> and
    /// <c>X.GetAwaiter().GetResult()</c>. <paramref name="producesValue"/> is false for <c>Wait()</c>, which is void.
    /// </summary>
    private static bool TryGetTaskExpression(ExpressionSyntax site, out ExpressionSyntax taskExpression, out bool producesValue)
    {
        taskExpression = null!;
        producesValue = true;

        switch (site)
        {
            case MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } result:
                taskExpression = result.Expression;
                return true;

            case InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Wait" } wait
            }:
                taskExpression = wait.Expression;
                producesValue = false;
                return true;

            case InvocationExpressionSyntax
            {
                ArgumentList.Arguments.Count: 0,
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "GetResult",
                    Expression: InvocationExpressionSyntax
                    {
                        ArgumentList.Arguments.Count: 0,
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } getAwaiter
                    }
                }
            }:
                taskExpression = getAwaiter.Expression;
                return true;

            default:
                return false;
        }
    }

    /// <summary><c>.Result</c> and <c>.Wait()</c> wrap failures in <c>AggregateException</c>; await and the sync API do not.</summary>
    private static bool IsInsideAggregateExceptionCatch(SyntaxNode site, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Block.Span.Contains(site.Span))
                continue;

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Declaration?.Type is { } type &&
                    semanticModel.GetTypeInfo(type, cancellationToken).Type is { Name: "AggregateException" } caught &&
                    caught.ContainingNamespace?.ToDisplayString() == "System")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<Document> CreateAwaitFixAsync(
        Document document,
        ExpressionSyntax site,
        ExpressionSyntax taskExpression,
        SyntaxAnnotation marker,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(taskExpression.WithoutTrivia());
        if (NeedsParentheses(site))
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);

        editor.ReplaceNode(
            site,
            replacement
                .WithTriviaFrom(site)
                .WithAdditionalAnnotations(Formatter.Annotation, marker));

        return await FormatAsync(editor.GetChangedDocument(), cancellationToken).ConfigureAwait(false);
    }

    private static bool NeedsParentheses(ExpressionSyntax site)
    {
        return site.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == site,
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression == site,
            InvocationExpressionSyntax invocation => invocation.Expression == site,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression == site,
            PostfixUnaryExpressionSyntax => true,
            _ => false
        };
    }

    /// <summary>The EF Core call the task comes from, when the site blocks on the call itself rather than a local.</summary>
    private static bool TryGetDirectEfInvocation(
        SemanticModel semanticModel,
        ExpressionSyntax taskExpression,
        CancellationToken cancellationToken,
        out IInvocationOperation efInvocation,
        out InvocationExpressionSyntax efSyntax)
    {
        efInvocation = null!;
        efSyntax = null!;

        if (semanticModel.GetOperation(taskExpression, cancellationToken) is not { } taskOperation ||
            BlockingEfAsyncCallAnalyzer.UnwrapTaskAdapters(taskOperation) is not IInvocationOperation invocation ||
            invocation.Syntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } syntax ||
            !BlockingEfAsyncCallAnalyzer.IsEfCoreAsyncOperation(invocation))
        {
            return false;
        }

        // A static-form extension call (EntityFrameworkQueryableExtensions.ToListAsync(query)) has no sync twin there.
        if (invocation.TargetMethod.IsExtensionMethod &&
            semanticModel.GetSymbolInfo(((MemberAccessExpressionSyntax)syntax.Expression).Expression, cancellationToken).Symbol is INamedTypeSymbol)
        {
            return false;
        }

        efInvocation = invocation;
        efSyntax = syntax;
        return true;
    }

    private static async Task<Document> CreateSyncFixAsync(
        Document document,
        ExpressionSyntax site,
        IInvocationOperation efInvocation,
        InvocationExpressionSyntax efSyntax,
        string syncName,
        SyntaxAnnotation marker,
        CancellationToken cancellationToken)
    {
        var memberAccess = (MemberAccessExpressionSyntax)efSyntax.Expression;

        // Drop the token argument: the synchronous method has no CancellationToken parameter.
        var arguments = efSyntax.ArgumentList.Arguments;
        foreach (var argument in efInvocation.Arguments)
        {
            if (argument.IsImplicit ||
                argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter?.Type is not { Name: "CancellationToken" } tokenType ||
                tokenType.ContainingNamespace?.ToDisplayString() != "System.Threading" ||
                argument.Syntax is not ArgumentSyntax tokenArgument)
            {
                continue;
            }

            arguments = arguments.Remove(tokenArgument);
        }

        var syncInvocation = efSyntax
            .WithExpression(memberAccess.WithName(SyntaxFactory.IdentifierName(syncName).WithTriviaFrom(memberAccess.Name)))
            .WithArgumentList(efSyntax.ArgumentList.WithArguments(arguments))
            .WithTriviaFrom(site)
            .WithAdditionalAnnotations(Formatter.Annotation, marker);

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(site, syncInvocation);

        // ToListAsync binds through Microsoft.EntityFrameworkCore alone; ToList needs System.Linq.
        if (efInvocation.TargetMethod.IsExtensionMethod)
        {
            var semanticModel = editor.SemanticModel;
            var namespaceName = syncName is "ExecuteDelete" or "ExecuteUpdate"
                ? QueryableExtensionNamespaceResolver.Resolve(semanticModel.Compilation, syncName)
                : "System.Linq";
            editor.EnsureUsingForExtensionMethod(
                semanticModel,
                site.SpanStart,
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type,
                syncName,
                namespaceName);
        }

        return await FormatAsync(editor.GetChangedDocument(), cancellationToken).ConfigureAwait(false);
    }

    private static Task<Document> FormatAsync(Document document, CancellationToken cancellationToken)
    {
        return Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken);
    }

    private static async Task<bool> IsAcceptableAsync(
        Document fixedDocument,
        SyntaxAnnotation marker,
        int errorsBefore,
        ITypeSymbol? originalType,
        CancellationToken cancellationToken)
    {
        var newModel = await fixedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = await fixedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (newModel == null || newRoot == null || CountErrors(newModel, cancellationToken) > errorsBefore)
            return false;

        if (originalType == null)
            return true;

        var replaced = newRoot.GetAnnotatedNodes(marker).OfType<ExpressionSyntax>().FirstOrDefault();
        var newType = replaced == null ? null : newModel.GetTypeInfo(replaced, cancellationToken).Type;

        // Types from two compilations are compared by their display, which includes type arguments and nullability.
        return newType != null &&
               newType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               originalType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>The synchronous call must be EF Core's or LINQ's, not an application method that happens to share the name.</summary>
    private static async Task<bool> BindsToEfOrLinqAsync(Document fixedDocument, SyntaxAnnotation marker, CancellationToken cancellationToken)
    {
        var newModel = await fixedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = await fixedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var replaced = newRoot?.GetAnnotatedNodes(marker).OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (newModel == null || replaced == null ||
            newModel.GetSymbolInfo(replaced, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var original = method.ReducedFrom ?? method;
        while (original.OverriddenMethod != null)
            original = original.OverriddenMethod;

        var namespaceName = original.ContainingNamespace?.ToDisplayString() ?? "";
        return namespaceName == "System.Linq" || namespaceName.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal);
    }

    private static async Task<Document> WithoutMarkerAsync(Document document, SyntaxAnnotation marker, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var marked = root?.GetAnnotatedNodes(marker).FirstOrDefault();
        return root == null || marked == null
            ? document
            : document.WithSyntaxRoot(root.ReplaceNode(marked, marked.WithoutAnnotations(marker)));
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
