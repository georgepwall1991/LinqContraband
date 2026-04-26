using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC022_ToListInSelectProjection;

/// <summary>
/// Provides code fixes for LC022. Removes collection materializer calls from inside Select projections.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToListInSelectProjectionFixer))]
[Shared]
public class ToListInSelectProjectionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ToListInSelectProjectionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        var invocation = node as InvocationExpressionSyntax
                         ?? node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();

        if (invocation == null) return;
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;
        if (!CanSafelyRemoveMaterializer(invocation, semanticModel, context.CancellationToken)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove collection materializer from projection",
                c => ApplyFixAsync(context.Document, invocation, c),
                "RemoveMaterializerFromProjection"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // The invocation is something like: expr.ToList()
        // We need to extract the receiver (expr)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return document;

        var receiver = memberAccess.Expression;

        editor.ReplaceNode(invocation, receiver
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia()));

        return editor.GetChangedDocument();
    }

    private static bool CanSafelyRemoveMaterializer(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        if (invocation.Ancestors().Any(ancestor =>
                ancestor is InitializerExpressionSyntax or AnonymousObjectMemberDeclaratorSyntax))
        {
            return false;
        }

        if (memberAccess.Name.Identifier.Text != "ToList") return false;

        var materializedType = semanticModel.GetTypeInfo(invocation, cancellationToken).Type;
        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        return SymbolEqualityComparer.Default.Equals(materializedType, receiverType);
    }
}
