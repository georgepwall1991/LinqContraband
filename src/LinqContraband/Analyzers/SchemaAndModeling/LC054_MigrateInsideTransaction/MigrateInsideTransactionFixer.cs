using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC054_MigrateInsideTransaction;

/// <summary>
/// Provides code fixes for LC054. Removes a transaction that exists only to wrap the Migrate call.
/// </summary>
/// <remarks>
/// The fix applies only when the transaction covers nothing but Migrate: the begin statement (or using statement),
/// the Migrate statement, and optionally one commit right after it. When other work shares the transaction, removing
/// it would change that work's atomicity, so the diagnostic is reported without a fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MigrateInsideTransactionFixer))]
[Shared]
public sealed class MigrateInsideTransactionFixer : CodeFixProvider
{
    private const string Title = "Remove the transaction around Migrate";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MigrateInsideTransactionAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocationSyntax = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationSyntax == null ||
                semanticModel.GetOperation(invocationSyntax, context.CancellationToken) is not IInvocationOperation migrate ||
                !MigrateInsideTransactionScope.TryGetMigrateFacade(migrate, out var facade))
            {
                continue;
            }

            var contextRoot = MigrateInsideTransactionScope.GetContextRoot(facade);
            if (contextRoot == null) continue;

            var transaction = MigrateInsideTransactionScope.FindOpenTransaction(migrate, contextRoot);
            if (transaction == null) continue;

            var newRoot = TryRemoveTransaction(root, migrate, transaction, contextRoot);
            if (newRoot == null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(newRoot)),
                    nameof(MigrateInsideTransactionFixer)),
                diagnostic);
        }
    }

    private static SyntaxNode? TryRemoveTransaction(
        SyntaxNode root,
        IInvocationOperation migrate,
        OpenTransaction transaction,
        ISymbol contextRoot)
    {
        if (transaction.Start is IUsingOperation usingOperation)
            return TryUnwrapUsingStatement(root, migrate, usingOperation, transaction, contextRoot);

        if (transaction.Start.Parent is not IBlockOperation block) return null;

        // tx = db.Database.BeginTransaction(): removing the assignment would leave the declaration unused.
        if (transaction.Start is IExpressionStatementOperation { Operation: IAssignmentOperation }) return null;

        var statements = block.Operations;
        var startIndex = statements.IndexOf(transaction.Start);
        if (startIndex < 0 || startIndex + 1 >= statements.Length) return null;
        if (!IsMigrateStatement(statements[startIndex + 1], migrate)) return null;

        var start = transaction.Start.Syntax;
        var migrateStatement = statements[startIndex + 1].Syntax;
        var removed = new List<SyntaxNode> { start };
        IOperation? commit = null;
        if (startIndex + 2 < statements.Length)
        {
            // Anything after Migrate that still runs inside the transaction keeps it.
            commit = statements[startIndex + 2];
            if (!MigrateInsideTransactionScope.IsCommitOf(commit, transaction, contextRoot)) return null;
            removed.Add(commit.Syntax);
        }

        if (!OnlyReferencedBy(block, transaction.Local, commit)) return null;

        // Migrate takes the removed statement's place, including any blank line or comment above it.
        var tracked = root.TrackNodes(removed.Concat(new[] { migrateStatement }));
        var currentMigrate = tracked.GetCurrentNode(migrateStatement)!;
        tracked = tracked.ReplaceNode(currentMigrate, currentMigrate.WithLeadingTrivia(start.GetLeadingTrivia()));
        return tracked.RemoveNodes(removed.Select(node => tracked.GetCurrentNode(node)!), SyntaxRemoveOptions.KeepNoTrivia);
    }

    private static SyntaxNode? TryUnwrapUsingStatement(
        SyntaxNode root,
        IInvocationOperation migrate,
        IUsingOperation usingOperation,
        OpenTransaction transaction,
        ISymbol contextRoot)
    {
        if (usingOperation.Syntax is not UsingStatementSyntax usingStatement) return null;

        IOperation migrateStatement;
        IOperation? commit = null;
        if (usingOperation.Body is IBlockOperation body)
        {
            if (body.Operations.Length is 0 or > 2) return null;
            migrateStatement = body.Operations[0];
            if (body.Operations.Length == 2)
            {
                commit = body.Operations[1];
                if (!MigrateInsideTransactionScope.IsCommitOf(commit, transaction, contextRoot)) return null;
            }
        }
        else
        {
            migrateStatement = usingOperation.Body;
        }

        if (!IsMigrateStatement(migrateStatement, migrate) ||
            migrateStatement.Syntax is not StatementSyntax statement ||
            !OnlyReferencedBy(usingOperation.Body, transaction.Local, commit))
        {
            return null;
        }

        var replacement = statement
            .WithLeadingTrivia(usingStatement.GetLeadingTrivia())
            .WithTrailingTrivia(usingStatement.GetTrailingTrivia());
        return root.ReplaceNode(usingStatement, replacement);
    }

    private static bool IsMigrateStatement(IOperation statement, IInvocationOperation migrate)
    {
        return statement is IExpressionStatementOperation expressionStatement &&
               ReferenceEquals(expressionStatement.Operation.UnwrapConversions(), migrate);
    }

    private static bool OnlyReferencedBy(IOperation scope, ILocalSymbol? local, IOperation? commit)
    {
        if (local == null) return true;

        foreach (var reference in scope.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local)) continue;
            if (commit != null && commit.Syntax.Span.Contains(reference.Syntax.Span)) continue;
            return false;
        }

        return true;
    }
}
