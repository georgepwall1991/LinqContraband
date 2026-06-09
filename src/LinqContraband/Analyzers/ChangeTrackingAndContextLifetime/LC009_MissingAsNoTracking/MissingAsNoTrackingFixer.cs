using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
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
public sealed class MissingAsNoTrackingFixer : CodeFixProvider
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
        var semanticModel = editor.SemanticModel;
        if (semanticModel == null) return document;

        var sourceExpression = FindEfSourceExpression(invocation, semanticModel, cancellationToken);

        if (sourceExpression == null) return document;

        if (IsInvocationOf(sourceExpression, "AsNoTracking")) return document;

        // sourceExpression is the DbSet source ("db.Users" or "db.Set<User>()").
        // We want to replace it with "<source>.AsNoTracking()".

        var asNoTracking = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            sourceExpression,
            SyntaxFactory.IdentifierName("AsNoTracking"));

        var asNoTrackingInvocation = SyntaxFactory.InvocationExpression(asNoTracking)
            .WithTriviaFrom(sourceExpression)
            .WithAdditionalAnnotations(Formatter.Annotation);

        editor.ReplaceNode(sourceExpression, asNoTrackingInvocation);

        editor.EnsureUsing("Microsoft.EntityFrameworkCore");

        return editor.GetChangedDocument();
    }

    // Walk the syntactic receiver chain of the materializer and return the innermost
    // expression whose type is a DbSet — that is the EF source to wrap with AsNoTracking().
    // A purely syntactic walk cannot distinguish the DbSet source "db.Set<T>()" (an invocation)
    // from an intermediate operator like ".Where(...)", so the semantic type is required.
    private static ExpressionSyntax? FindEfSourceExpression(
        ExpressionSyntax node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax? source = null;

        for (ExpressionSyntax? current = node; current != null; current = GetReceiverExpression(current))
        {
            var type = semanticModel.GetTypeInfo(current, cancellationToken).Type;
            if (type.IsDbSet())
                source = current;
        }

        return source;
    }

    private static ExpressionSyntax? GetReceiverExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            InvocationExpressionSyntax invocation => invocation.Expression,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            _ => null
        };
    }

    private static bool IsInvocationOf(ExpressionSyntax expression, string methodName)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax ma)
            return ma.Name.Identifier.Text == methodName;

        return false;
    }
}
