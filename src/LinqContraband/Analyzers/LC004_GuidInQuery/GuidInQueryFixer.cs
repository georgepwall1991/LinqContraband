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

namespace LinqContraband.Analyzers.LC004_GuidInQuery;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GuidInQueryFixer))]
[Shared]
public class GuidInQueryFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(GuidInQueryAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root?.FindNode(diagnosticSpan);
        if (node == null) return;

        // Node could be ObjectCreationExpression or InvocationExpression
        // We treat it generically as ExpressionSyntax for extraction
        if (node is ExpressionSyntax expression)
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Extract to local variable",
                    c => ExtractToLocalVariableAsync(context.Document, expression, c),
                    "ExtractGuidToLocal"),
                diagnostic);
    }

    private async Task<Document> ExtractToLocalVariableAsync(Document document, ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        // Find the statement containing the query that contains this expression
        // We need to insert the variable declaration BEFORE this statement
        var statement = expression.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null) return document;

        // Create the variable declaration: var guid = Guid.NewGuid();
        var variableName = "guid"; // Simplify for now, collision handling would be improved in production

        // If the expression is complex, we might want to check for naming conflicts, but sticking to simple "guid"
        // We can improve uniqueness if needed.
        var variableDeclarator = SyntaxFactory.VariableDeclarator(variableName)
            .WithInitializer(SyntaxFactory.EqualsValueClause(expression.WithoutTrivia()));

        var variableDeclaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(variableDeclarator));

        var localDeclaration = SyntaxFactory.LocalDeclarationStatement(variableDeclaration)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Insert the declaration before the statement
        editor.InsertBefore(statement, localDeclaration);

        // Replace the original expression with usage of the variable
        editor.ReplaceNode(expression, SyntaxFactory.IdentifierName(variableName));

        return editor.GetChangedDocument();
    }
}