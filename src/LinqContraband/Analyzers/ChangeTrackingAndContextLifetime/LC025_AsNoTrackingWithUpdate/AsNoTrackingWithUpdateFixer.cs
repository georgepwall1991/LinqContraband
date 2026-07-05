using System.Collections.Generic;
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

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

/// <summary>
/// Provides code fixes for LC025. Removes AsNoTracking from the source query.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsNoTrackingWithUpdateFixer))]
[Shared]
public sealed class AsNoTrackingWithUpdateFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsNoTrackingWithUpdateAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var argument = token.Parent.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
        if (argument == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        var local = GetLocalArgumentSymbol(semanticModel, argument, context.CancellationToken);
        if (local == null) return;

        if (FindAsNoTrackingOrigin(root, semanticModel, local, argument.SpanStart, context.CancellationToken) == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove AsNoTracking from query",
                c => ApplyFixAsync(context.Document, argument, c),
                "RemoveAsNoTracking"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var local = GetLocalArgumentSymbol(semanticModel, argument, cancellationToken);
        if (local == null) return document;

        var asNoTrackingInvocation = FindAsNoTrackingOrigin(root, semanticModel, local, argument.SpanStart, cancellationToken);
        if (asNoTrackingInvocation?.Expression is not MemberAccessExpressionSyntax asNoTrackingAccess) return document;

        if (asNoTrackingAccess.Expression is ExpressionSyntax source)
        {
            editor.ReplaceNode(asNoTrackingInvocation, source.WithTriviaFrom(asNoTrackingInvocation));
        }

        return editor.GetChangedDocument();
    }

    private static ILocalSymbol? GetLocalArgumentSymbol(SemanticModel semanticModel, ArgumentSyntax argument, CancellationToken cancellationToken)
    {
        var operation = semanticModel.GetOperation(argument.Expression, cancellationToken)?.UnwrapConversions();
        if (operation is ILocalReferenceOperation localReference)
            return localReference.Local;

        return semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol as ILocalSymbol;
    }

    private static InvocationExpressionSyntax? FindAsNoTrackingOrigin(
        SyntaxNode root,
        SemanticModel semanticModel,
        ILocalSymbol local,
        int boundary,
        CancellationToken cancellationToken)
    {
        var origins = new List<AsNoTrackingOrigin>();

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer == null || declarator.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), local)) continue;

            AddOrigin(declarator.Initializer.Value, declarator.SpanStart);
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.SpanStart >= boundary) continue;

            var target = semanticModel.GetOperation(assignment.Left, cancellationToken)?.UnwrapConversions();
            if (target is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            AddOrigin(assignment.Right, assignment.SpanStart);
        }

        foreach (var forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (!forEach.Span.Contains(boundary) || forEach.Expression.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(forEach, cancellationToken), local)) continue;

            AddOrigin(forEach.Expression, forEach.Expression.SpanStart);
        }

        if (origins.Count == 0) return null;

        var best = origins[0];
        for (var i = 1; i < origins.Count; i++)
        {
            if (origins[i].Position >= best.Position)
                best = origins[i];
        }

        if (IsConditionalRelativeTo(best.Syntax, boundary, root) &&
            origins.Any(origin => origin.Position < best.Position))
        {
            return null;
        }

        return best.Invocation;

        void AddOrigin(ExpressionSyntax expression, int position)
        {
            if (!IsNoTrackingSource(root, semanticModel, expression, position, cancellationToken, new HashSet<ISymbol>(SymbolEqualityComparer.Default)))
                return;

            var invocation = FindAsNoTrackingInvocation(expression);
            origins.Add(new AsNoTrackingOrigin(position, invocation, expression));
        }
    }

    private readonly struct AsNoTrackingOrigin
    {
        public AsNoTrackingOrigin(int position, InvocationExpressionSyntax? invocation, SyntaxNode syntax)
        {
            Position = position;
            Invocation = invocation;
            Syntax = syntax;
        }

        public int Position { get; }
        public InvocationExpressionSyntax? Invocation { get; }
        public SyntaxNode Syntax { get; }
    }

    private static bool IsNoTrackingSource(
        SyntaxNode root,
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        int boundary,
        CancellationToken cancellationToken,
        ISet<ISymbol> visited)
    {
        var operation = semanticModel.GetOperation(expression, cancellationToken)?.UnwrapConversions();
        if (operation == null)
            return false;

        if (operation is IInvocationOperation invocation)
        {
            if (HasAsNoTrackingInChain(invocation))
                return true;

            if (invocation.TargetMethod.Name.IsMaterializerMethod() &&
                invocation.GetInvocationReceiver() is ILocalReferenceOperation receiverLocal)
            {
                return IsLocalFromNoTracking(root, semanticModel, receiverLocal.Local, boundary, cancellationToken, visited);
            }
        }

        return operation is ILocalReferenceOperation localReference &&
               IsLocalFromNoTracking(root, semanticModel, localReference.Local, boundary, cancellationToken, visited);
    }

    private static bool IsLocalFromNoTracking(
        SyntaxNode root,
        SemanticModel semanticModel,
        ILocalSymbol local,
        int boundary,
        CancellationToken cancellationToken,
        ISet<ISymbol> visited)
    {
        if (!visited.Add(local)) return false;

        AsNoTrackingOrigin? bestOrigin = null;

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer == null || declarator.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), local)) continue;

            if (bestOrigin == null || declarator.SpanStart >= bestOrigin.Value.Position)
                bestOrigin = new AsNoTrackingOrigin(declarator.SpanStart, null, declarator.Initializer.Value);
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.SpanStart >= boundary) continue;

            var target = semanticModel.GetOperation(assignment.Left, cancellationToken)?.UnwrapConversions();
            if (target is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (bestOrigin == null || assignment.SpanStart >= bestOrigin.Value.Position)
                bestOrigin = new AsNoTrackingOrigin(assignment.SpanStart, null, assignment.Right);
        }

        return bestOrigin != null &&
               bestOrigin.Value.Syntax is ExpressionSyntax expression &&
               IsNoTrackingSource(root, semanticModel, expression, bestOrigin.Value.Position, cancellationToken, visited);
    }

    private static bool HasAsNoTrackingInChain(IInvocationOperation invocation)
    {
        IOperation current = invocation;
        while (current.UnwrapConversions() is IInvocationOperation currentInvocation)
        {
            if (IsEfCoreNoTrackingDirective(currentInvocation.TargetMethod))
                return true;

            if (IsEfCoreAsTracking(currentInvocation.TargetMethod))
                return false;

            var receiver = currentInvocation.GetInvocationReceiver();
            if (receiver == null)
                return false;

            current = receiver;
        }

        return false;
    }

    private static bool IsEfCoreNoTrackingDirective(IMethodSymbol method)
    {
        if (method.Name is not ("AsNoTracking" or "AsNoTrackingWithIdentityResolution"))
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsEfCoreAsTracking(IMethodSymbol method)
    {
        if (method.Name != "AsTracking")
            return false;

        var namespaceName = method.ContainingNamespace?.ToString();
        return namespaceName == "Microsoft.EntityFrameworkCore" ||
               namespaceName?.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal) == true;
    }

    private static bool IsConditionalRelativeTo(SyntaxNode originSyntax, int usePosition, SyntaxNode rootSyntax)
    {
        for (var node = originSyntax.Parent; node != null && node != rootSyntax; node = node.Parent)
        {
            var isBranching = node is IfStatementSyntax
                or SwitchStatementSyntax
                or SwitchExpressionSyntax
                or ConditionalExpressionSyntax
                or CatchClauseSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or CommonForEachStatementSyntax;

            if (isBranching && !node.Span.Contains(usePosition))
                return true;
        }

        return false;
    }

    private static InvocationExpressionSyntax? FindAsNoTrackingInvocation(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText is "AsNoTracking" or "AsNoTrackingWithIdentityResolution" &&
                invocation.ArgumentList.Arguments.Count == 0);
    }
}
