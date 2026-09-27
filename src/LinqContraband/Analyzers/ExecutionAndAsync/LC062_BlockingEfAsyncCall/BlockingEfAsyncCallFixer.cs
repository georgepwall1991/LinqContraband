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
            // Neither rewrite keeps a #pragma, #if or other directive inside the replaced expression.
            if (ContainsDirective(site)) continue;

            var siteOperation = semanticModel.GetOperation(site, cancellationToken);
            if (siteOperation == null) continue;

            var originalType = producesValue ? semanticModel.GetTypeInfo(site, cancellationToken).Type : null;

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);

            // In async code: await the task.
            if (!SyncBlockerFixer.IsInvalidAwaitContext(site) &&
                !SyncBlockerFixer.WouldStrandRefStructLocal(site, semanticModel, cancellationToken) &&
                !WouldStrandRefLikeValue(site, semanticModel, cancellationToken))
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
            // An application override of the async method (SaveChangesAsync auditing, say) would be skipped by the
            // synchronous call unless the synchronous method is overridden as well.
            if (BypassesAsyncOverride(efInvocation, semanticModel.Compilation, cancellationToken))
                continue;

            // The synchronous call drops the token argument, so evaluating it must have no effect worth keeping.
            if (!TokenArgumentsAreSideEffectFree(efInvocation))
                continue;

            var syncMarker = new SyntaxAnnotation();
            var synchronousDraft = await CreateSyncFixAsync(document, site, efInvocation, efSyntax, syncName, syncMarker, cancellationToken)
                .ConfigureAwait(false);
            if (!await IsAcceptableAsync(synchronousDraft, syncMarker, errorsBefore.Value, originalType, cancellationToken).ConfigureAwait(false) ||
                !await BindsToEfOrLinqAsync(synchronousDraft, syncMarker, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!await CallsTheMatchingOverloadAsync(synchronousDraft, syncMarker, efInvocation.TargetMethod, cancellationToken).ConfigureAwait(false))
                continue;

            var synchronous = await WithoutMarkerAsync(synchronousDraft, syncMarker, cancellationToken).ConfigureAwait(false);

            context.RegisterCodeFix(
                CodeAction.Create($"Call {syncName} instead of blocking", _ => Task.FromResult(synchronous), nameof(BlockingEfAsyncCallFixer)),
                diagnostic);
        }
    }

    private static bool ContainsDirective(ExpressionSyntax site)
    {
        return site.DescendantTrivia(site.Span).Any(trivia =>
            trivia.HasStructure || trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia));
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

    /// <summary>
    /// <c>.Result</c> and <c>.Wait()</c> wrap failures in <c>AggregateException</c>; await and the sync API do not.
    /// True when a <c>try</c> around the site, in the same member, has a catch that can observe that exception: an
    /// untyped <c>catch</c>, <c>catch (Exception)</c>, <c>catch (SystemException)</c>, or a catch of
    /// <c>AggregateException</c> or one of its base types, with or without a filter.
    /// </summary>
    private static bool IsInsideAggregateExceptionCatch(SyntaxNode site, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var aggregate = semanticModel.Compilation.GetTypeByMetadataName("System.AggregateException");

        foreach (var ancestor in site.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax)
                break;

            if (ancestor is not TryStatementSyntax tryStatement || !tryStatement.Block.Span.Contains(site.Span))
                continue;

            foreach (var catchClause in tryStatement.Catches)
            {
                if (catchClause.Declaration?.Type is not { } type)
                    return true;

                if (semanticModel.GetTypeInfo(type, cancellationToken).Type is not INamedTypeSymbol caught)
                    return true;

                if (caught.Name is "Exception" or "SystemException" &&
                    caught.ContainingNamespace?.ToDisplayString() == "System")
                {
                    return true;
                }

                for (var current = aggregate; current != null; current = current.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, caught))
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
                .WithLeadingTrivia(site.GetLeadingTrivia())
                .WithTrailingTrivia(KeepComments(site, taskExpression).AddRange(site.GetTrailingTrivia()))
                .WithAdditionalAnnotations(Formatter.Annotation, marker));

        return await FormatAsync(editor.GetChangedDocument(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the site is in a ref struct, or when a ref-like parameter or local, or a <c>ref</c> or
    /// <c>ref readonly</c> local, declared before the site is read after it: an inserted <c>await</c> would keep it
    /// live across the suspension point, which the async rewriter rejects (CS4007, CS4012, CS9217). LC008's helper
    /// covers ref-like locals; this adds the rest.
    /// </summary>
    private static bool WouldStrandRefLikeValue(SyntaxNode site, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // Inside a ref struct, `this` can be read implicitly (a bare field or method name), so any member of one is
        // treated as keeping a ref-like value live.
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken)?.ContainingType is { IsRefLikeType: true })
            return true;

        var body = site.Ancestors().FirstOrDefault(node =>
            node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);
        if (body == null)
            return false;

        foreach (var node in body.DescendantNodes())
        {
            if (node.SpanStart <= site.Span.End)
                continue;

            if (node is not IdentifierNameSyntax identifier)
                continue;

            switch (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol)
            {
                case IParameterSymbol { Type.IsRefLikeType: true }:
                    return true;
                // A ref-like local, or a ref / ref readonly local, cannot be live across an await (CS4007, CS9217).
                case ILocalSymbol local
                    when (local.Type.IsRefLikeType || local.RefKind != RefKind.None) &&
                         local.DeclaringSyntaxReferences.Any(reference => reference.Span.Start < site.SpanStart):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The comments the rewrite would drop: those after <paramref name="kept"/> inside <paramref name="site"/>, as in
    /// <c>db.Users.ToListAsync() /* why */ .Result</c>. They move after the new expression, each followed by a line
    /// break when it is a single-line comment, so it cannot swallow the rest of the line.
    /// </summary>
    private static SyntaxTriviaList KeepComments(ExpressionSyntax site, SyntaxNode kept)
    {
        var comments = new System.Collections.Generic.List<SyntaxTrivia>();
        var lastToken = site.GetLastToken();

        void Add(SyntaxTriviaList trivia)
        {
            foreach (var item in trivia)
            {
                if (!item.IsKind(SyntaxKind.SingleLineCommentTrivia) && !item.IsKind(SyntaxKind.MultiLineCommentTrivia))
                    continue;

                comments.Add(SyntaxFactory.Space);
                comments.Add(item);
                if (item.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    comments.Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
            }
        }

        Add(kept.GetLastToken().TrailingTrivia);
        foreach (var token in site.DescendantTokens())
        {
            if (token.SpanStart < kept.Span.End)
                continue;

            Add(token.LeadingTrivia);
            if (token != lastToken)
                Add(token.TrailingTrivia);
        }

        return SyntaxFactory.TriviaList(comments);
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
                !IsCancellationToken(argument.Parameter?.Type) ||
                argument.Syntax is not ArgumentSyntax tokenArgument)
            {
                continue;
            }

            arguments = arguments.Remove(tokenArgument);
        }

        var syncInvocation = efSyntax
            .WithExpression(memberAccess.WithName(SyntaxFactory.IdentifierName(syncName).WithTriviaFrom(memberAccess.Name)))
            .WithArgumentList(efSyntax.ArgumentList.WithArguments(arguments))
            .WithLeadingTrivia(site.GetLeadingTrivia())
            .WithTrailingTrivia(KeepComments(site, efSyntax).AddRange(site.GetTrailingTrivia()))
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

    /// <summary>
    /// True when every explicit <c>CancellationToken</c> argument can be dropped without losing an effect: a local, a
    /// parameter, a static field or a field of <c>this</c>, a local or a parameter, <c>default</c>, a constant, or
    /// <c>CancellationToken.None</c>. A call such as <c>ToListAsync(GetToken())</c> would lose the call, and a
    /// property read such as <c>source.Token</c> can run code (it throws once the source is disposed).
    /// </summary>
    private static bool TokenArgumentsAreSideEffectFree(IInvocationOperation efInvocation)
    {
        foreach (var argument in efInvocation.Arguments)
        {
            if (argument.IsImplicit || argument.ArgumentKind != ArgumentKind.Explicit || !IsCancellationToken(argument.Parameter?.Type))
                continue;

            if (!IsSideEffectFree(argument.Value))
                return false;
        }

        return true;
    }

    private static bool IsSideEffectFree(IOperation? operation)
    {
        switch (operation?.UnwrapConversions())
        {
            case ILocalReferenceOperation or IParameterReferenceOperation or IDefaultValueOperation:
                return true;
            case { ConstantValue.HasValue: true }:
                return true;
            case IFieldReferenceOperation field:
                return field.Instance?.UnwrapConversions() is null
                    or IInstanceReferenceOperation
                    or ILocalReferenceOperation
                    or IParameterReferenceOperation;
            case IPropertyReferenceOperation { Instance: null, Property: { Name: "None", IsStatic: true } property }:
                return IsCancellationToken(property.ContainingType);
            default:
                return false;
        }
    }

    private static bool IsCancellationToken(ITypeSymbol? type)
    {
        return type is { Name: "CancellationToken" } && type.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }

    /// <summary>
    /// For an instance method that can be overridden, the synchronous call must bind to the overload with the async
    /// method's parameters minus the token (<c>SaveChangesAsync(bool, CancellationToken)</c> to <c>SaveChanges(bool)</c>),
    /// the one <see cref="SyncOverridesCoverAsyncOverrides"/> paired it with.
    /// </summary>
    private static async Task<bool> CallsTheMatchingOverloadAsync(
        Document fixedDocument,
        SyntaxAnnotation marker,
        IMethodSymbol asyncMethod,
        CancellationToken cancellationToken)
    {
        if (asyncMethod.IsStatic || asyncMethod.IsExtensionMethod)
            return true;

        var newModel = await fixedDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = await fixedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var replaced = newRoot?.GetAnnotatedNodes(marker).OfType<InvocationExpressionSyntax>().FirstOrDefault();
        return newModel != null && replaced != null &&
               newModel.GetSymbolInfo(replaced, cancellationToken).Symbol is IMethodSymbol syncMethod &&
               SignatureWithoutToken(syncMethod) == SignatureWithoutToken(asyncMethod);
    }

    /// <summary>The parameter types other than <c>CancellationToken</c>, with ref kinds, so overloads compare by signature.</summary>
    private static string SignatureWithoutToken(IMethodSymbol method)
    {
        return string.Join(
            ",",
            method.Parameters
                .Where(parameter => !IsCancellationToken(parameter.Type))
                .Select(parameter => parameter.RefKind + " " + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    /// <summary>
    /// The synchronous call must be EF Core's or LINQ's, not an application method that happens to share the name:
    /// after following overrides (an application context's <c>SaveChanges</c> override counts as <c>DbContext</c>'s,
    /// which the override checks cover), the method must be declared by <c>System.Linq.Enumerable</c>,
    /// <c>System.Linq.Queryable</c>, or a type in an EF Core assembly. A project type in a
    /// <c>Microsoft.EntityFrameworkCore.*</c> namespace does not count.
    /// </summary>
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

        var declaringType = original.ContainingType;
        var compilation = newModel.Compilation;
        if (SymbolEqualityComparer.Default.Equals(declaringType, compilation.GetTypeByMetadataName("System.Linq.Enumerable")) ||
            SymbolEqualityComparer.Default.Equals(declaringType, compilation.GetTypeByMetadataName("System.Linq.Queryable")))
        {
            return true;
        }

        return declaringType?.ContainingAssembly?.Name is { } assemblyName &&
               assemblyName.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the call resolves to an application override of the async method, when the receiver's static type
    /// is unsealed and another assembly could derive from it (a public context, <c>DbContext</c>, <c>DbSet&lt;T&gt;</c>),
    /// or when the receiver's type, or a type in this compilation derived from it, overrides the async method at a
    /// more derived level than the synchronous overload with the same parameters: the synchronous call could then skip
    /// an override. Every async overload counts, because <c>SaveChangesAsync(CancellationToken)</c> calls the
    /// overridable <c>SaveChangesAsync(bool, CancellationToken)</c>.
    /// </summary>
    private static bool BypassesAsyncOverride(
        IInvocationOperation efInvocation,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var method = efInvocation.TargetMethod;
        if (method.IsStatic || method.IsExtensionMethod || efInvocation.Instance?.Type is not INamedTypeSymbol receiverType)
            return false;

        var asyncName = method.Name;
        var syncName = asyncName.Substring(0, asyncName.Length - "Async".Length);

        // The chain from the receiver's type covers an override the call itself resolves to.
        if (!SyncOverridesCoverAsyncOverrides(receiverType, asyncName, syncName))
            return true;

        if (receiverType.IsSealed)
            return false;

        // An unsealed type another assembly can derive from may hold an object that overrides only the async method.
        if (!IsClosedToOtherAssemblies(receiverType, compilation))
            return true;

        // A variable of a base type can hold a derived context declared in this project.

        foreach (var symbol in compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type, cancellationToken))
        {
            if (symbol is INamedTypeSymbol candidate &&
                DerivesFrom(candidate, receiverType) &&
                !SyncOverridesCoverAsyncOverrides(candidate, asyncName, syncName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when no other assembly can derive from <paramref name="type"/>: it is declared in this compilation, it or
    /// a containing type is private or internal, and the assembly grants no <c>InternalsVisibleTo</c>.
    /// </summary>
    private static bool IsClosedToOtherAssemblies(INamedTypeSymbol type, Compilation compilation)
    {
        if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            return false;

        var hidden = false;
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Internal or Accessibility.ProtectedAndInternal)
                hidden = true;
        }

        return hidden &&
               !compilation.Assembly.GetAttributes().Any(attribute =>
                   attribute.AttributeClass is { Name: "InternalsVisibleToAttribute" } attributeClass &&
                   attributeClass.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices");
    }

    /// <summary>
    /// Walking from <paramref name="type"/> towards EF Core's own base type, every override of an async overload
    /// needs an override of the synchronous overload with the same parameters minus the token, declared at the same
    /// level or a more derived one. Overloads are matched by signature, not name: a context that overrides
    /// <c>SaveChangesAsync(bool, CancellationToken)</c> and only <c>SaveChanges()</c> would have
    /// <c>SaveChanges(bool)</c> skip its async code. A chain that overrides neither is fine.
    /// </summary>
    private static bool SyncOverridesCoverAsyncOverrides(INamedTypeSymbol type, string asyncName, string syncName)
    {
        var syncOverrides = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current != null && !IsEfCoreType(current); current = current.BaseType)
        {
            foreach (var member in current.GetMembers(syncName))
            {
                if (member is IMethodSymbol { IsOverride: true } syncOverride)
                    syncOverrides.Add(SignatureWithoutToken(syncOverride));
            }

            foreach (var member in current.GetMembers(asyncName))
            {
                if (member is IMethodSymbol { IsOverride: true } asyncOverride &&
                    !syncOverrides.Contains(SignatureWithoutToken(asyncOverride)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType.OriginalDefinition))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A type EF Core itself ships, where the override walk stops: declared in an assembly named
    /// <c>Microsoft.EntityFrameworkCore*</c> and read from metadata, so an application type in a
    /// <c>Microsoft.EntityFrameworkCore.*</c> namespace is still walked.
    /// </summary>
    private static bool IsEfCoreType(INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences.IsEmpty &&
               type.ContainingAssembly?.Name is { } assemblyName &&
               assemblyName.StartsWith("Microsoft.EntityFrameworkCore", System.StringComparison.Ordinal);
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
