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

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

/// <summary>
/// Provides code fixes for LC016. Extracts DateTime.Now/UtcNow to a local variable to prevent multiple evaluations in LINQ queries.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidDateTimeNowFixer))]
[Shared]
public class AvoidDateTimeNowFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidDateTimeNowAnalyzer.DiagnosticId);

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

        // We expect a MemberAccessExpression (DateTime.Now)
        // But it might be wrapped? The analyzer reports on the operation syntax.
        // Usually it is SimpleMemberAccessExpression "DateTime.Now"
        var memberAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (memberAccess == null) return;

        // Find the containing statement to insert before
        var statement = memberAccess.FirstAncestorOrSelf<StatementSyntax>();
        if (statement == null) return;

        // Ensure the statement is in a Block so we can insert before it
        if (statement.Parent is not BlockSyntax) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Extract to local variable",
                c => FixAsync(context.Document, memberAccess, statement, c),
                nameof(AvoidDateTimeNowFixer)),
            diagnostic);
    }

    private async Task<Document> FixAsync(Document document, MemberAccessExpressionSyntax memberAccess,
        StatementSyntax statement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return document;

#pragma warning disable CS0618 // Type or member is obsolete - Workspace constructor deprecated but Services not available in older Roslyn
        var editor = new SyntaxEditor(root, document.Project.Solution.Workspace);
#pragma warning restore CS0618
        var generator = editor.Generator;

        // 1. Determine variable name
        var baseName = "now";
        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName == "UtcNow") baseName = "utcNow";

        // Generate unique name
        var variableName = GenerateUniqueName(baseName, statement, semanticModel);

        // 2. Create the variable declaration: var now = DateTime.Now;
        // We use the exact expression text from the source to preserve "DateTime" vs "System.DateTime" etc.
        var replacementValue = memberAccess.WithoutTrivia();

        var varDeclaration = generator.LocalDeclarationStatement(
            generator.TypeExpression(SpecialType
                .System_Object), // "var" (represented as object type usually implies var in generator, or use explicit var)
            variableName,
            replacementValue);

        // Force "var" if possible, generator usually handles this with null type or specific flags, 
        // but Roslyn's generator behavior varies. 
        // Let's stick to explicitly creating the syntax to be sure it's "var name = value;"
        var declarationSyntax = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.IdentifierName("var"))
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(
                                    SyntaxFactory.Identifier(variableName))
                                .WithInitializer(
                                    SyntaxFactory.EqualsValueClause(replacementValue))
                        )))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"))
            .WithAdditionalAnnotations(Formatter.Annotation);

        // 3. Replace the usage with the variable name
        var identifier = generator.IdentifierName(variableName).WithTriviaFrom(memberAccess);

        // 4. Apply changes
        // We use editor to batch changes if we were doing multiple, but here we do manual replace on root usually?
        // Actually, SyntaxEditor is easier for "InsertBefore".

        editor.InsertBefore(statement, declarationSyntax);
        editor.ReplaceNode(memberAccess, identifier);

        return document.WithSyntaxRoot(editor.GetChangedRoot());
    }

    private string GenerateUniqueName(string baseName, SyntaxNode location, SemanticModel semanticModel)
    {
        var name = baseName;
        var index = 1;

        while (semanticModel.LookupSymbols(location.SpanStart, name: name).Any()) name = $"{baseName}{index++}";

        return name;
    }
}
