using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Provides code fixes for LC032. Rewrites a tracked bulk-update foreach loop into a single
/// set-based <c>ExecuteUpdate</c>/<c>ExecuteUpdateAsync</c> call. The trailing
/// <c>SaveChanges</c> is left in place (it becomes a no-op for the converted rows but still
/// flushes any unrelated pending changes), and a warning comment flags the behaviour change.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExecuteUpdateForBulkUpdatesFixer))]
[Shared]
public sealed partial class ExecuteUpdateForBulkUpdatesFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExecuteUpdateForBulkUpdatesAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var forEach = root.FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<ForEachStatementSyntax>()
            .FirstOrDefault();

        if (forEach is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        if (!TryBuildPlan(forEach, semanticModel, out var plan))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteUpdate()",
                c => ApplyFixAsync(context.Document, forEach, plan, c),
                "UseExecuteUpdate"),
            diagnostic);
    }

    private static bool TryBuildPlan(ForEachStatementSyntax forEach, SemanticModel semanticModel, out RewritePlan plan)
    {
        plan = null!;

        // A local-variable source (pre-materialized list or query local) would orphan the
        // local and/or produce a type-invalid receiver, so decline it in v1. The first check is
        // a fast path; the post-strip check is the load-bearing one (a bare identifier is never
        // stripped, but the receiver under a materializer could itself be an identifier).
        if (forEach.Expression is IdentifierNameSyntax)
            return false;

        var receiver = StripCollectionMaterializer(forEach.Expression);
        if (receiver is IdentifierNameSyntax)
            return false;

        if (HasUnsupportedExecuteUpdateReceiverStep(receiver))
            return false;

        var receiverType = semanticModel.GetTypeInfo(receiver).Type;
        if (receiverType is null || (!receiverType.IsIQueryable() && !receiverType.IsDbSet()))
            return false;

        if (!TryGetSetters(forEach, forEach.Identifier.Text, out var setters))
            return false;

        if (!TryClassifyTrailingSaveChanges(forEach, semanticModel, out var trailingIsAwaited, out var cancellationTokenText))
            return false;

        var mode = DetermineRewriteMode(forEach, semanticModel, trailingIsAwaited);
        if (mode == RewriteMode.None)
            return false;

        // A token can only be preserved on an awaited ExecuteUpdateAsync overload that accepts
        // one. If the trailing SaveChanges carried a token but the rewrite would be synchronous
        // (e.g. an unawaited SaveChangesAsync(token)) or no token-accepting overload exists,
        // decline rather than silently drop the developer's cancellation intent.
        if (cancellationTokenText is not null &&
            (mode != RewriteMode.Async || !HasExecuteUpdateAsyncTokenOverload(semanticModel.Compilation)))
        {
            return false;
        }

        plan = new RewritePlan(
            receiver.WithoutTrivia().ToString(),
            setters,
            forEach.Identifier.Text,
            mode == RewriteMode.Async,
            ResolveExecuteUpdateNamespace(semanticModel.Compilation, mode == RewriteMode.Async),
            mode == RewriteMode.Async ? cancellationTokenText : null);
        return true;
    }

    private sealed class RewritePlan
    {
        public RewritePlan(string receiverText, ImmutableArray<(string Left, string Right)> setters, string iterationName, bool isAsync, string? importNamespace, string? cancellationTokenText)
        {
            ReceiverText = receiverText;
            Setters = setters;
            IterationName = iterationName;
            IsAsync = isAsync;
            ImportNamespace = importNamespace;
            CancellationTokenText = cancellationTokenText;
        }

        public string ReceiverText { get; }

        public ImmutableArray<(string Left, string Right)> Setters { get; }

        public string IterationName { get; }

        public bool IsAsync { get; }

        public string? ImportNamespace { get; }

        public string? CancellationTokenText { get; }
    }
}
