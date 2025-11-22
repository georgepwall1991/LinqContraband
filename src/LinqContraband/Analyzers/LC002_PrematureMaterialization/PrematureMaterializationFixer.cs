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

namespace LinqContraband
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PrematureMaterializationFixer)), Shared]
    public class PrematureMaterializationFixer : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(PrematureMaterializationAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var invocation = root?.FindNode(diagnosticSpan) as InvocationExpressionSyntax;

            if (invocation == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Move filtering before materialization",
                    createChangedDocument: c => MoveFilterBeforeMaterializationAsync(context.Document, invocation, c),
                    equivalenceKey: "MoveFilterBeforeMaterialization"),
                diagnostic);
        }

        private async Task<Document> MoveFilterBeforeMaterializationAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess)) return document;
            if (!(memberAccess.Expression is InvocationExpressionSyntax materializeInvocation)) return document;
            if (!(materializeInvocation.Expression is MemberAccessExpressionSyntax materializeMemberAccess)) return document;

            var source = materializeMemberAccess.Expression; 

            var newFilterInvocation = invocation.WithExpression(
                memberAccess.WithExpression(source)
            );

            var materializeName = materializeMemberAccess.Name;
            
            var newRoot = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    newFilterInvocation,
                    materializeName
                )
            ).WithArgumentList(materializeInvocation.ArgumentList)
             .WithLeadingTrivia(invocation.GetLeadingTrivia())
             .WithTrailingTrivia(invocation.GetTrailingTrivia()); 

            // Add Formatting annotation manually if not working via factory
            newRoot = newRoot.WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);

            editor.ReplaceNode(invocation, newRoot);

            return editor.GetChangedDocument();
        }
    }
}
