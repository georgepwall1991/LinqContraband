using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MultipleOrderByFixer))]
[Shared]
public class MultipleOrderByFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MultipleOrderByAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Replace with ThenBy/ThenByDescending",
                    c => ReplaceWithThenByAsync(context.Document, invocation, c),
                    nameof(MultipleOrderByFixer)),
                diagnostic);
        }
    }

    private async Task<Document> ReplaceWithThenByAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var methodName = memberAccess.Name.Identifier.Text;
        var newMethodName = methodName == "OrderBy" ? "ThenBy" : "ThenByDescending";

        var newName = SyntaxFactory.IdentifierName(newMethodName);
        var newMemberAccess = memberAccess.WithName(newName);
        var newInvocation = invocation.WithExpression(newMemberAccess);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}
