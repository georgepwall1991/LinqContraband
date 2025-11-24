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

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Provides code fixes for LC009. Adds AsNoTracking() to read-only Entity Framework queries to improve performance.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingAsNoTrackingFixer))]
[Shared]
public class MissingAsNoTrackingFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingAsNoTrackingAnalyzer.DiagnosticId);

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
                "Add AsNoTracking()",
                c => AddAsNoTrackingAsync(context.Document, invocation, c),
                "AddAsNoTracking"),
            diagnostic);
    }

    private async Task<Document> AddAsNoTrackingAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        var sourceExpression = GetSourceExpression(invocation);

        if (sourceExpression == null) return document;

        if (IsInvocationOf(sourceExpression, "AsNoTracking")) return document;

        // sourceExpression is "db.Users"
        // We want to replace "db.Users" with "db.Users.AsNoTracking()"

        var asNoTracking = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            sourceExpression,
            SyntaxFactory.IdentifierName("AsNoTracking"));

        var asNoTrackingInvocation = SyntaxFactory.InvocationExpression(asNoTracking)
            .WithTriviaFrom(sourceExpression)
            .WithAdditionalAnnotations(Formatter.Annotation);

        editor.ReplaceNode(sourceExpression, asNoTrackingInvocation);

        EnsureUsing(editor, "Microsoft.EntityFrameworkCore");

        return editor.GetChangedDocument();
    }

    private ExpressionSyntax? GetSourceExpression(ExpressionSyntax node)
    {
        if (node is InvocationExpressionSyntax invocation) return GetSourceExpression(invocation.Expression);

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            // If this MemberAccess is the method name of an Invocation, keep digging.
            // e.g. .Where(...) -> Parent is Invocation.
            if (memberAccess.Parent is InvocationExpressionSyntax) return GetSourceExpression(memberAccess.Expression);

            // If parent is NOT an invocation (e.g. it's another MemberAccess or Return), 
            // then THIS is the property/field we want (e.g. db.Users).
            return memberAccess;
        }

        return node; // Fallback for Identifiers or other expressions
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }

    private static void EnsureUsing(DocumentEditor editor, string namespaceName)
    {
        var root = editor.OriginalRoot as CompilationUnitSyntax;
        if (root == null) return;

        // Check if using exists in ORIGINAL root first
        if (root.Usings.Any(u =>
                u.Name?.ToString() == namespaceName ||
                (u.Alias != null && u.Name?.ToString() == namespaceName)))
            return;

        // Add using to the CURRENT root (which might have other changes pending)
        // DocumentEditor handles root changes cumulatively if we use its API,
        // but here we were manually replacing the root node. 
        // DocumentEditor doesn't have a dedicated AddUsing method.
        // But we can replace the CompilationUnit with a new one that has the Using.

        // Better approach: Use the editor to replace the CompilationUnit syntax node itself
        // but we must be careful not to invalidate previous edits if we replace the whole root?
        // DocumentEditor tracks nodes. 

        // Actually, the safest way with DocumentEditor is to perform node-level edits.
        // But adding a using is a root-level edit.

        // If we use editor.ReplaceNode(root, newRoot), it replaces the entire tree.
        // If we do this AFTER the node replacement, it might work IF the editor
        // can track the nodes in the new tree? No, it can't track across full tree replacements easily.

        // However, reordering to do ReplaceNode FIRST (as done above) ensures the node reference 'sourceExpression'
        // is still valid when ReplaceNode is called.
        // Then we call EnsureUsing. EnsureUsing calls editor.ReplaceNode(root, ...).
        // This replaces the root. The previous ReplaceNode is queued.
        // Does DocumentEditor support multiple replacements where one is the root?
        // "ReplaceNode" logs an edit.
        // If we log an edit for a child, and then an edit for the root, 
        // DocumentEditor might conflict or the root replacement might overwrite the child replacement
        // if the "newRoot" we pass doesn't contain the child replacement.
        // But `root.AddUsings` creates a NEW tree from the OLD root (without the child replacement).

        // So, simply reordering might NOT be enough if `AddUsings` operates on `editor.OriginalRoot`.
        // We are taking OriginalRoot (clean), adding a using -> NewRoot1.
        // We are taking SourceExpression (from OriginalRoot), replacing it -> Edit1.
        // If we tell Editor: "Replace Root with NewRoot1" AND "Replace SourceExpression with X",
        // The "Replace Root" might win or conflict.

        // Standard practice with DocumentEditor for adding usings:
        // There isn't a built-in method.
        // We should use `ImportAdder` service if available, but that's in Workspaces.
        // Or, since we are in a Fixer, we can just manipulate the root directly?
        // But we want to use DocumentEditor for the node replacement.

        // Let's try to insert the using directive into the Usings list instead of replacing the root.
        // CompilationUnitSyntax has a Usings property.
        // We can try: editor.InsertBefore(root.Members.First(), usingDirective) ?
        // Or editor.InsertAfter(root.Usings.Last(), usingDirective).

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));

        if (root.Usings.Any())
        {
            editor.InsertAfter(root.Usings.Last(), usingDirective);
        }
        else if (root.Members.Any())
        {
            // Insert before the first member (namespace or class)
            editor.InsertBefore(root.Members.First(), usingDirective);
        }
        else
        {
            // Empty file? Edge case.
            // Fallback to replacing root if we can't insert.
            var newRoot = root.AddUsings(usingDirective);
            editor.ReplaceNode(root, newRoot);
        }
    }
}
