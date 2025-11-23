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

        EnsureUsing(editor, "Microsoft.EntityFrameworkCore");

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

        return editor.GetChangedDocument();
    }

    private ExpressionSyntax? GetSourceExpression(ExpressionSyntax node)
    {
        if (node is InvocationExpressionSyntax invocation)
        {
            return GetSourceExpression(invocation.Expression);
        }

        if (node is MemberAccessExpressionSyntax memberAccess)
        {
            // If this MemberAccess is the method name of an Invocation, keep digging.
            // e.g. .Where(...) -> Parent is Invocation.
            if (memberAccess.Parent is InvocationExpressionSyntax)
            {
                return GetSourceExpression(memberAccess.Expression);
            }
            
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
