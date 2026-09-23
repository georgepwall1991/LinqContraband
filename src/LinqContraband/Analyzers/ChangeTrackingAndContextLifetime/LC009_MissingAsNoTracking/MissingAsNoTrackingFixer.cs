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

        var invocation = root?.FindNode(diagnosticSpan, getInnermostNodeForTie: true) as InvocationExpressionSyntax;
        if (invocation == null) return;

        // The entities leave the method: a caller or callee may change and save them, and
        // AsNoTracking() would silently drop that save. Leave the decision to a person.
        if (diagnostic.Properties.ContainsKey(MissingAsNoTrackingAnalyzer.EntitiesEscapeProperty)) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        // No place in the chain where AsNoTracking() compiles: report without a fix.
        var sourceExpression = FindInsertionPoint(invocation, semanticModel, context.CancellationToken);
        if (sourceExpression == null || IsInvocationOf(sourceExpression, "AsNoTracking")) return;

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

        var sourceExpression = FindInsertionPoint(invocation, semanticModel, cancellationToken);

        if (sourceExpression == null) return document;

        if (IsInvocationOf(sourceExpression, "AsNoTracking")) return document;

        // sourceExpression is the DbSet source ("db.Users" or "db.Set<User>()"), or the last
        // DbSet-only operator in the chain ("db.Users.FromSqlRaw(...)").
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

    // Walk the syntactic receiver chain of the materializer and return the expression to wrap
    // with AsNoTracking(). AsNoTracking() turns a DbSet<T> into an IQueryable<T>, so it must go
    // after the last operator that only accepts a DbSet<T> (FromSqlRaw, FromSql,
    // FromSqlInterpolated, the SQL Server Temporal* operators, project helpers taking a DbSet):
    // "db.Users.AsNoTracking().FromSqlRaw(...)" does not compile. When the chain has no such
    // operator, wrap the innermost DbSet-typed expression, the EF source itself. A purely
    // syntactic walk cannot tell the DbSet source "db.Set<T>()" (an invocation) from an
    // intermediate operator like ".Where(...)", so the semantic model is required.
    private static ExpressionSyntax? FindInsertionPoint(
        InvocationExpressionSyntax materializer,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax? source = null;

        for (var current = GetReceiverExpression(materializer); current != null; current = GetReceiverExpression(current))
        {
            if (current is InvocationExpressionSyntax call &&
                semanticModel.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol method &&
                GetRequiredReceiverType(method).IsDbSet())
            {
                // The outermost DbSet-only operator wins. If it does not hand back a query,
                // there is nowhere AsNoTracking() can go.
                return semanticModel.GetTypeInfo(call, cancellationToken).Type.IsIQueryable() ? call : null;
            }

            var type = semanticModel.GetTypeInfo(current, cancellationToken).Type;
            if (type.IsDbSet())
                source = current;
        }

        return source;
    }

    // The type the call's receiver must have: the "this" parameter of an extension method
    // (called either way), or the declaring type of an instance method.
    private static ITypeSymbol? GetRequiredReceiverType(IMethodSymbol method)
    {
        if (method.ReducedFrom != null)
            return method.ReceiverType;
        if (method.IsExtensionMethod)
            return method.Parameters.Length > 0 ? method.Parameters[0].Type : null;
        return method.IsStatic ? null : method.ContainingType;
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
