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

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Provides code fixes for LC023. Replaces FirstOrDefault/SingleOrDefault primary key lookups with Find/FindAsync.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FindInsteadOfFirstOrDefaultFixer))]
[Shared]
public class FindInsteadOfFirstOrDefaultFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FindInsteadOfFirstOrDefaultAnalyzer.DiagnosticId);

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

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Find/FindAsync",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseFind"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if (firstArg is LambdaExpressionSyntax lambda)
            {
                ExpressionSyntax? valueExpression = null;
                if (lambda.Body is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.EqualsExpression))
                {
                    // Basic logic: if left is property access, right is the value (or vice versa)
                    // The analyzer already verified it's a PK equality.
                    if (binary.Left is MemberAccessExpressionSyntax)
                        valueExpression = binary.Right;
                    else
                        valueExpression = binary.Left;
                }

                if (valueExpression != null)
                {
                    var methodName = memberAccess.Name.Identifier.Text;
                    var isAsync = methodName.EndsWith("Async");
                    var newMethodName = isAsync ? "FindAsync" : "Find";

                    var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(newMethodName));
                    var newArguments = SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(valueExpression)));
                    
                    var newInvocation = SyntaxFactory.InvocationExpression(newMemberAccess, newArguments)
                        .WithLeadingTrivia(invocation.GetLeadingTrivia())
                        .WithTrailingTrivia(invocation.GetTrailingTrivia());

                    editor.ReplaceNode(invocation, newInvocation);
                }
            }
        }

        return editor.GetChangedDocument();
    }
}
