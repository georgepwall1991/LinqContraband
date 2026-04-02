using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

/// <summary>
/// Provides code fixes for LC017. Adds .Select() projection before the materializer to load only accessed properties.
/// </summary>
/// <remarks>
/// The fixer analyzes the usage pattern of the materialized collection to determine which properties are accessed,
/// then generates an anonymous type projection containing only those properties.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WholeEntityProjectionFixer))]
[Shared]
public sealed partial class WholeEntityProjectionFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(WholeEntityProjectionAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root?.FindNode(diagnosticSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return;

        if (!TryCreateProjectionFixContext(root!, invocation, semanticModel, context.CancellationToken, out var fixContext))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Add .Select() with anonymous type ({fixContext.AccessedProperties.Count} properties)",
                c => AddSelectProjectionAsync(context.Document, fixContext, c),
                nameof(WholeEntityProjectionFixer) + "_AnonymousType"),
            diagnostic);
    }

    private sealed class ProjectionFixContext
    {
        public ProjectionFixContext(InvocationExpressionSyntax invocation, IReadOnlyCollection<string> accessedProperties)
        {
            Invocation = invocation;
            AccessedProperties = accessedProperties;
        }

        public InvocationExpressionSyntax Invocation { get; }
        public IReadOnlyCollection<string> AccessedProperties { get; }
    }
}
