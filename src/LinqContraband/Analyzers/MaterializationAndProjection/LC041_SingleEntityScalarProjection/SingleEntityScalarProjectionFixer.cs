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
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC041_SingleEntityScalarProjection;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingleEntityScalarProjectionFixer))]
[Shared]
public sealed partial class SingleEntityScalarProjectionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SingleEntityScalarProjectionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.Span == diagnostic.Location.SourceSpan);

        if (invocation == null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        if (!TryGetFixContext(invocation, semanticModel, out var fixContext))
            return;

        if (!IsSafeFixMaterializer(invocation))
            return;

        if (HasUnsupportedPredicateArgument(invocation, semanticModel, context.CancellationToken))
            return;

        if (!fixContext.IsVarDeclaration)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Project consumed scalar before materializing",
                c => ApplyFixAsync(context.Document, invocation, fixContext, c),
                "ProjectConsumedScalar"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        FixContext fixContext,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var newInvocation = RewriteInvocation(invocation, fixContext.PropertyName);
        editor.ReplaceNode(invocation, newInvocation.WithTriviaFrom(invocation));

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel != null)
        {
            foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                var operation = semanticModel.GetOperation(memberAccess, cancellationToken) as IPropertyReferenceOperation;
                if (operation == null)
                    continue;

                if (operation.Property.Name != fixContext.PropertyName)
                    continue;

                if (operation.Instance?.UnwrapConversions() is not ILocalReferenceOperation localReference ||
                    !SymbolEqualityComparer.Default.Equals(localReference.Local, fixContext.Local))
                {
                    continue;
                }

                editor.ReplaceNode(memberAccess, SyntaxFactory.IdentifierName(fixContext.Local.Name).WithTriviaFrom(memberAccess));
            }
        }

        return editor.GetChangedDocument();
    }

}
