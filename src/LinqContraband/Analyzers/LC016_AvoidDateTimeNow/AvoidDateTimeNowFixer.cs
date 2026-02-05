using System.Collections.Generic;
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

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

/// <summary>
/// Provides code fixes for LC016. Extracts DateTime.Now to a local variable.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidDateTimeNowFixer))]
[Shared]
public class AvoidDateTimeNowFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidDateTimeNowAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var memberAccess = token.Parent.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        if (memberAccess == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract to local variable",
                c => ApplyFixAsync(context.Document, memberAccess, c),
                "ExtractToLocal"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // Find the statement containing the expression
        var statement = memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null) return document;

        // Create a unique variable name
        var variableName = GetUniqueVariableName(memberAccess);

        // Create the variable declaration: var now = DateTime.Now;
        var newVariable = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(
                        SyntaxFactory.Identifier(variableName),
                        null,
                        SyntaxFactory.EqualsValueClause(memberAccess.WithoutTrivia())
                    )
                )
            )
        ).WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        // Replace the member access with the variable reference
        editor.ReplaceNode(memberAccess, SyntaxFactory.IdentifierName(variableName).WithTriviaFrom(memberAccess));

        // Insert the declaration before the statement
        editor.InsertBefore(statement, newVariable);

        return editor.GetChangedDocument();
    }

    private static string GetUniqueVariableName(SyntaxNode node)
    {
        var existingNames = new HashSet<string>();

        var block = node.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        if (block != null)
        {
            foreach (var descendant in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                existingNames.Add(descendant.Identifier.Text);
            }
        }

        const string baseName = "now";
        if (!existingNames.Contains(baseName)) return baseName;

        for (var i = 1; i < 100; i++)
        {
            var candidate = baseName + i;
            if (!existingNames.Contains(candidate)) return candidate;
        }

        return baseName;
    }
}
