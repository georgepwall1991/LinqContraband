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

namespace LinqContraband.Analyzers.LC056_StoredProcedureComposed;

/// <summary>
/// Provides code fixes for LC056. Inserts <c>.AsEnumerable()</c>, or <c>.AsAsyncEnumerable()</c> when only that
/// compiles, just before the composing operator so it and the rest of the chain run in memory.
/// </summary>
/// <remarks>
/// The inserted call changes the query's static type from <c>IQueryable&lt;T&gt;</c> to an enumerable, which breaks
/// operators such as <c>Include</c>, async terminals without an async LINQ library, and assignments to
/// <c>IQueryable&lt;T&gt;</c>. The fixer compiles the rewritten document and only offers a rewrite that adds no errors.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StoredProcedureComposedFixer))]
[Shared]
public sealed class StoredProcedureComposedFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(StoredProcedureComposedAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        var errorsBefore = CountErrors(semanticModel, context.CancellationToken);

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic sits on the composing operator's name; switch to memory just before that operator,
            // after any pass-through calls such as AsNoTracking() that need the queryable.
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not SimpleNameSyntax
                {
                    Parent: MemberAccessExpressionSyntax { Expression: var receiver } memberAccess
                } ||
                memberAccess.Parent is not InvocationExpressionSyntax)
            {
                continue;
            }

            foreach (var method in new[] { "AsEnumerable", "AsAsyncEnumerable" })
            {
                var newRoot = root.ReplaceNode(receiver, Append(receiver, method));
                var newDocument = document.WithSyntaxRoot(newRoot);
                var newModel = await newDocument.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                if (newModel == null || CountErrors(newModel, context.CancellationToken) > errorsBefore) continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Run the rest of the query in memory with {method}()",
                        _ => Task.FromResult(newDocument),
                        nameof(StoredProcedureComposedFixer)),
                    diagnostic);
                break;
            }
        }
    }

    private static InvocationExpressionSyntax Append(ExpressionSyntax receiver, string method)
    {
        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver.WithoutTrailingTrivia(),
                    SyntaxFactory.IdentifierName(method)))
            .WithTrailingTrivia(receiver.GetTrailingTrivia());
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
