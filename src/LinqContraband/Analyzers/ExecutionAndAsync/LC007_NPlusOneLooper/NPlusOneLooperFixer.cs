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
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Provides conservative code fixes for LC007. Converts unconditional explicit loading inside foreach loops into eager loading.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NPlusOneLooperFixer))]
[Shared]
public sealed class NPlusOneLooperFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(NPlusOneLooperAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var invocation = root.FindNode(diagnosticSpan) as InvocationExpressionSyntax
                         ?? root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
                             .OfType<InvocationExpressionSyntax>()
                             .FirstOrDefault();
        if (invocation == null)
            return;

        var fixContext = await TryCreateFixContextAsync(context.Document, invocation, context.CancellationToken).ConfigureAwait(false);
        if (fixContext == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Include() and remove per-item load",
                c => ApplyFixAsync(context.Document, diagnostic.Location.SourceSpan, c),
                "UseIncludeForExplicitLoad"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, TextSpan invocationSpan, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var invocation = root.FindNode(invocationSpan) as InvocationExpressionSyntax
                         ?? root.FindToken(invocationSpan.Start).Parent?.AncestorsAndSelf()
                             .OfType<InvocationExpressionSyntax>()
                             .FirstOrDefault();
        if (invocation == null)
            return document;

        var fixContext = await TryCreateFixContextAsync(document, invocation, cancellationToken).ConfigureAwait(false);
        if (fixContext == null)
            return document;

        var currentLoadStatement = root.FindNode(fixContext.LoadStatement.Span) as ExpressionStatementSyntax
                                   ?? root.FindToken(fixContext.LoadStatement.Span.Start).Parent?.AncestorsAndSelf()
                                       .OfType<ExpressionStatementSyntax>()
                                       .FirstOrDefault();
        if (currentLoadStatement == null)
            return document;

        var removedLoadRoot = root.RemoveNode(currentLoadStatement, SyntaxRemoveOptions.KeepNoTrivia);
        if (removedLoadRoot == null)
            return document;

        var currentQueryTarget = removedLoadRoot.FindNode(fixContext.QueryTargetNode.Span) as ExpressionSyntax;
        if (currentQueryTarget == null)
            return document;

        var updatedRoot = removedLoadRoot.ReplaceNode(currentQueryTarget, fixContext.RewrittenQuerySource);
        var updatedDocument = document.WithSyntaxRoot(updatedRoot);
        var editor = await DocumentEditor.CreateAsync(updatedDocument, cancellationToken).ConfigureAwait(false);
        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        return editor.GetChangedDocument();
    }

    private static async Task<ExplicitLoadFixContext?> TryCreateFixContextAsync(
        Document document,
        InvocationExpressionSyntax loadInvocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return null;

        if (!TryGetDirectLoadStatement(loadInvocation, out var loadStatement))
            return null;

        var loop = loadStatement.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault();
        if (loop == null)
            return null;

        if (!IsDirectLoopStatement(loop, loadStatement))
            return null;

        if (ContainsUnsafeControlFlow(loop.Statement))
            return null;

        if (CountExplicitLoads(loop.Statement) != 1)
            return null;

        var loopVariableName = loop.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(loopVariableName))
            return null;

        if (!TryGetNavigationLambda(loadInvocation, loopVariableName, out var navigationLambda))
            return null;

        if (!TryResolveQuerySourceTarget(loop.Expression, semanticModel, cancellationToken, out var queryTargetNode, out var querySourceExpression))
            return null;

        if (!TryAddInclude(querySourceExpression, navigationLambda, semanticModel, cancellationToken, out var rewrittenQuerySource))
            return null;

        return new ExplicitLoadFixContext(loadStatement, queryTargetNode, rewrittenQuerySource);
    }

    private static bool TryGetDirectLoadStatement(
        InvocationExpressionSyntax invocation,
        out ExpressionStatementSyntax loadStatement)
    {
        loadStatement = invocation.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault()!;
        return loadStatement != null;
    }

    private static bool IsDirectLoopStatement(ForEachStatementSyntax loop, StatementSyntax statement)
    {
        if (loop.Statement is BlockSyntax block)
            return block.Statements.Contains(statement);

        return loop.Statement == statement;
    }

    private static bool ContainsUnsafeControlFlow(StatementSyntax loopBody)
    {
        return loopBody.DescendantNodes().Any(node =>
            node is IfStatementSyntax or SwitchStatementSyntax or ReturnStatementSyntax or BreakStatementSyntax or
                ContinueStatementSyntax or ThrowStatementSyntax or TryStatementSyntax or GotoStatementSyntax or
                YieldStatementSyntax);
    }

    private static int CountExplicitLoads(StatementSyntax loopBody)
    {
        return loopBody.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(IsExplicitLoadInvocation);
    }

    private static bool IsExplicitLoadInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Name.Identifier.Text is "Load" or "LoadAsync";
    }

    private static bool TryGetNavigationLambda(
        InvocationExpressionSyntax loadInvocation,
        string loopVariableName,
        out LambdaExpressionSyntax navigationLambda)
    {
        navigationLambda = null!;

        if (loadInvocation.Expression is not MemberAccessExpressionSyntax loadMember ||
            loadMember.Expression is not InvocationExpressionSyntax accessInvocation ||
            accessInvocation.Expression is not MemberAccessExpressionSyntax accessMember ||
            accessMember.Expression is not InvocationExpressionSyntax entryInvocation ||
            entryInvocation.Expression is not MemberAccessExpressionSyntax entryMember)
        {
            return false;
        }

        if (accessMember.Name.Identifier.Text is not ("Collection" or "Reference"))
            return false;

        if (entryMember.Name.Identifier.Text != "Entry" || entryInvocation.ArgumentList.Arguments.Count != 1)
            return false;

        if (entryInvocation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax identifier)
            return false;

        if (identifier.Identifier.ValueText != loopVariableName)
            return false;

        if (accessInvocation.ArgumentList.Arguments.Count != 1 ||
            accessInvocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        navigationLambda = lambda;
        return true;
    }

    private static bool TryResolveQuerySourceTarget(
        ExpressionSyntax loopExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax queryTargetNode,
        out ExpressionSyntax querySourceExpression)
    {
        queryTargetNode = null!;
        querySourceExpression = null!;

        if (loopExpression is IdentifierNameSyntax identifier &&
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local &&
            TryGetLocalInitializerExpression(local, semanticModel, cancellationToken, out var initializerExpression))
        {
            queryTargetNode = initializerExpression;
            querySourceExpression = initializerExpression;
            return true;
        }

        queryTargetNode = loopExpression;
        querySourceExpression = loopExpression;
        return true;
    }

    private static bool TryGetLocalInitializerExpression(
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializerExpression)
    {
        initializerExpression = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var executableRoot = declarator.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (executableRoot == null)
            return false;

        foreach (var assignment in executableRoot.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Span.Contains(declarator.Span))
                continue;

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, local))
                return false;
        }

        initializerExpression = declarator.Initializer.Value;
        return true;
    }

    private static bool TryAddInclude(
        ExpressionSyntax querySourceExpression,
        LambdaExpressionSyntax navigationLambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax rewrittenExpression)
    {
        rewrittenExpression = null!;

        if (querySourceExpression is not InvocationExpressionSyntax terminalInvocation ||
            terminalInvocation.Expression is not MemberAccessExpressionSyntax terminalMember)
        {
            return false;
        }

        var source = terminalMember.Expression;
        if (semanticModel.GetTypeInfo(source, cancellationToken).Type?.IsIQueryable() != true)
            return false;

        var includeMember = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source.WithoutTrivia(),
            SyntaxFactory.IdentifierName("Include"));

        var includeInvocation = SyntaxFactory.InvocationExpression(
                includeMember,
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(navigationLambda.WithoutTrivia()))))
            .WithTriviaFrom(source);

        rewrittenExpression = terminalInvocation
            .WithExpression(terminalMember.WithExpression(includeInvocation))
            .WithTriviaFrom(querySourceExpression);

        return true;
    }

    private sealed class ExplicitLoadFixContext
    {
        public ExplicitLoadFixContext(
            ExpressionStatementSyntax loadStatement,
            ExpressionSyntax queryTargetNode,
            ExpressionSyntax rewrittenQuerySource)
        {
            LoadStatement = loadStatement;
            QueryTargetNode = queryTargetNode;
            RewrittenQuerySource = rewrittenQuerySource;
        }

        public ExpressionStatementSyntax LoadStatement { get; }
        public ExpressionSyntax QueryTargetNode { get; }
        public ExpressionSyntax RewrittenQuerySource { get; }
    }
}
