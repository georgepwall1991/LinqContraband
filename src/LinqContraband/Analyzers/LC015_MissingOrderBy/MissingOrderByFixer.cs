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

namespace LinqContraband.Analyzers.LC015_MissingOrderBy;

/// <summary>
/// Provides code fixes for LC015. Injects OrderBy(x => x.Id) before unordered Skip/Last calls.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingOrderByFixer))]
[Shared]
public class MissingOrderByFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingOrderByAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        // Pre-check if we can determine the primary key before registering the fix
        // This prevents offering a code fix when we can't reliably generate valid code
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;

        var sourceExpression = memberAccess.Expression;
        var sourceType = semanticModel.GetTypeInfo(sourceExpression).Type;

        ITypeSymbol? entityType = null;
        if (sourceType is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            entityType = namedType.TypeArguments[0];

        if (entityType == null) return;

        var keyName = entityType.TryFindPrimaryKey();
        if (keyName == null) return; // Don't offer fix if we can't determine the primary key

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add OrderBy",
                c => AddOrderByAsync(context.Document, invocation, keyName, c),
                nameof(MissingOrderByFixer)),
            diagnostic);
    }

    private async Task<Document> AddOrderByAsync(Document document, InvocationExpressionSyntax invocation,
        string keyName, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var sourceExpression = memberAccess.Expression;

        // Generate: .OrderBy(x => x.{keyName})
        var generator = editor.Generator;

        // Lambda: x => x.Id
        var lambdaParamName = "x";
        var lambda = generator.ValueReturningLambdaExpression(
            lambdaParamName,
            generator.MemberAccessExpression(generator.IdentifierName(lambdaParamName), keyName)
        );

        // Expression: source.OrderBy(...)
        var orderByInvocation = generator.InvocationExpression(
            generator.MemberAccessExpression(sourceExpression, "OrderBy"),
            lambda
        );

        // Replace the original source expression (e.g. 'db.Users') with 'db.Users.OrderBy(x => x.Id)'
        // But wait, 'sourceExpression' is inside 'invocation'.
        // Example: db.Users.Skip(10)
        // sourceExpression = db.Users
        // invocation = db.Users.Skip(10)
        // We want: db.Users.OrderBy(x => x.Id).Skip(10)

        // If we replace sourceExpression, we are modifying the tree correctly.
        editor.ReplaceNode(sourceExpression, orderByInvocation);

        EnsureUsing(editor, "System.Linq");

        return editor.GetChangedDocument();
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
