using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace LinqContraband.Analyzers.LC002_PrematureMaterialization;

/// <summary>
/// Provides code fixes for LC002. Moves filtering operations before materialization to improve query performance.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PrematureMaterializationFixer))]
[Shared]
public class PrematureMaterializationFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PrematureMaterializationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindNode(diagnosticSpan) as InvocationExpressionSyntax;

        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Move filtering before materialization",
                c => MoveFilterBeforeMaterializationAsync(context.Document, invocation, c),
                "MoveFilterBeforeMaterialization"),
            diagnostic);
    }

    private async Task<Document> MoveFilterBeforeMaterializationAsync(Document document,
        InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess)) return document;
        if (!(memberAccess.Expression is InvocationExpressionSyntax materializeInvocation)) return document;
        if (!(materializeInvocation.Expression is MemberAccessExpressionSyntax materializeMemberAccess))
            return document;

        // Check if the PARENT of the Filter is ALSO a Materializer (e.g. ToList/ToArray)
        var parentIsMaterializer = false;
        if (invocation.Parent is MemberAccessExpressionSyntax parentMemberAccess &&
            parentMemberAccess.Parent is InvocationExpressionSyntax)
        {
            var name = parentMemberAccess.Name.Identifier.Text;
            if (name == "ToList" || name == "ToArray") parentIsMaterializer = true;
        }

        var source = materializeMemberAccess.Expression;

        var newFilterInvocation = invocation.WithExpression(
            memberAccess.WithExpression(source)
        );

        SyntaxNode newRoot;

        if (parentIsMaterializer)
        {
            // If parent is materializer, we don't need to append another one.
            // Just return the filter invocation: source.Where(...)
            // The outer parent will handle the materialization.
            newRoot = newFilterInvocation
                .WithLeadingTrivia(invocation.GetLeadingTrivia())
                .WithTrailingTrivia(invocation.GetTrailingTrivia());
        }
        else
        {
            var materializeName = materializeMemberAccess.Name;

            newRoot = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        newFilterInvocation,
                        materializeName
                    )
                ).WithArgumentList(materializeInvocation.ArgumentList)
                .WithLeadingTrivia(invocation.GetLeadingTrivia())
                .WithTrailingTrivia(invocation.GetTrailingTrivia());
        }

        // Add Formatting annotation manually if not working via factory
        newRoot = newRoot.WithAdditionalAnnotations(Formatter.Annotation);

        editor.ReplaceNode(invocation, newRoot);

        return editor.GetChangedDocument();
    }
}
