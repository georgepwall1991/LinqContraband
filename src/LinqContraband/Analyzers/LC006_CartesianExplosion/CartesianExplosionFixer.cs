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

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CartesianExplosionFixer))]
[Shared]
public class CartesianExplosionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CartesianExplosionAnalyzer.DiagnosticId);

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
                "Use AsSplitQuery()",
                c => ApplyFixAsync(context.Document, invocation, c),
                "UseAsSplitQuery"),
            diagnostic);
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        EnsureUsing(editor, "Microsoft.EntityFrameworkCore");

        // Target: .Include(Roles)
        // We want to insert .AsSplitQuery() before this call.
        // Currently: Source.Include(Roles)
        // New: Source.AsSplitQuery().Include(Roles)

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;
        var source = memberAccess.Expression;

        if (IsInvocationOf(source, "AsSplitQuery")) return document;

        // Create .AsSplitQuery() invocation
        var asSplitQuery = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            source,
            SyntaxFactory.IdentifierName("AsSplitQuery"));

        var asSplitQueryInvocation = SyntaxFactory.InvocationExpression(asSplitQuery);

        // Replace 'source' in the original expression with 'source.AsSplitQuery()', preserving trivia
        editor.ReplaceNode(source, asSplitQueryInvocation.WithTriviaFrom(source));

        return editor.GetChangedDocument();
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
        {
            return ma.Name.Identifier.Text == methodName;
        }

        return false;
    }

    private static void EnsureUsing(DocumentEditor editor, string namespaceName)
    {
        var root = editor.OriginalRoot as CompilationUnitSyntax;
        if (root == null) return;
        if (root.Usings.Any(u =>
                u.Name?.ToString() == namespaceName ||
                (u.Alias != null && u.Name?.ToString() == namespaceName)))
            return;

        var newRoot = root.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName)));
        editor.ReplaceNode(root, newRoot);
    }
}
