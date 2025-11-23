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

namespace LinqContraband.Analyzers.LC001_LocalMethod;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalMethodFixer))]
[Shared]
public class LocalMethodFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(LocalMethodAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract to variable (Manual semantic fix required)",
                c => ExtractToVariableAsync(context.Document, invocation, c),
                "ExtractToVariable"),
            diagnostic);
    }

    private async Task<Document> ExtractToVariableAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement == null) return document;

        var variableName = "value";

        // Create: var value = CalculateAge(u.Dob);
        var varType = SyntaxFactory.IdentifierName("var");
        var variableDeclaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    varType,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                                SyntaxFactory.Identifier(variableName))
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(invocation.WithoutTrivia()))
                    )))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n")); // Ensure newline matches test expectation on *nix

        // Replacement: value
        var replacement = SyntaxFactory.IdentifierName(variableName)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        editor.InsertBefore(statement, variableDeclaration);
        editor.ReplaceNode(invocation, replacement);

        return editor.GetChangedDocument();
    }
}