using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// Provides conservative code fixes for LC007. Converts unconditional explicit loading inside foreach loops into eager loading.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NPlusOneLooperFixer))]
[Shared]
public sealed partial class NPlusOneLooperFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(NPlusOneLooperAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var invocation = FindInvocation(root, diagnosticSpan);
        if (invocation == null)
            return;

        var fixContext = await TryCreateFixContextAsync(context.Document, invocation, context.CancellationToken).ConfigureAwait(false);
        if (fixContext == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Include() and remove per-item load",
                c => ApplyFixAsync(context.Document, diagnostic.Location.SourceSpan, c),
                "UseIncludeForExplicitLoad"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, TextSpan invocationSpan, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var invocation = FindInvocation(root, invocationSpan);
        if (invocation == null)
            return document;

        var fixContext = await TryCreateFixContextAsync(document, invocation, cancellationToken).ConfigureAwait(false);
        if (fixContext == null)
            return document;

        return await ApplyExplicitLoadFixAsync(document, root, fixContext, cancellationToken).ConfigureAwait(false);
    }
}
