using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace LinqContraband.Extensions;

/// <summary>
/// The Fix All provider every LinqContraband fixer returns: the batch fixer, plus support for a Fix All
/// request that names no code action. <c>dotnet format</c> asks the fixer for the first finding of a rule and
/// passes the equivalence key of whatever it offers, which is no key at all when that finding is one the fixer
/// deliberately leaves alone (LC009 when the entities leave the method, for example). The batch fixer then
/// matches no action and fixes nothing anywhere, so one unfixable finding stopped every other one from being
/// fixed. Such a request now uses the action offered for the first finding that has a fix. An IDE always
/// passes the key of the action the user picked, so Fix All there works exactly as before.
/// </summary>
internal sealed class LinqContrabandFixAllProvider : FixAllProvider
{
    public static FixAllProvider Instance { get; } = new LinqContrabandFixAllProvider();

    private LinqContrabandFixAllProvider()
    {
    }

    public override IEnumerable<FixAllScope> GetSupportedFixAllScopes() =>
        WellKnownFixAllProviders.BatchFixer.GetSupportedFixAllScopes();

    public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
    {
        if (fixAllContext.CodeActionEquivalenceKey is null)
        {
            var action = await FindFirstActionAsync(fixAllContext).ConfigureAwait(false);
            if (action is null)
                return null;

            if (action.EquivalenceKey is { } key)
                fixAllContext = WithEquivalenceKey(fixAllContext, key);
        }

        return await WellKnownFixAllProviders.BatchFixer.GetFixAsync(fixAllContext).ConfigureAwait(false);
    }

    private static async Task<CodeAction?> FindFirstActionAsync(FixAllContext fixAllContext)
    {
        var cancellationToken = fixAllContext.CancellationToken;
        foreach (var diagnostic in await GetDiagnosticsInScopeAsync(fixAllContext).ConfigureAwait(false))
        {
            if (diagnostic.Location.SourceTree is not { } tree ||
                fixAllContext.Solution.GetDocument(tree) is not { } document)
            {
                continue;
            }

            CodeAction? first = null;
            var context = new CodeFixContext(document, diagnostic, (action, _) => first ??= action, cancellationToken);
            await fixAllContext.CodeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            if (first is not null)
                return first;
        }

        return null;
    }

    private static async Task<IEnumerable<Diagnostic>> GetDiagnosticsInScopeAsync(FixAllContext fixAllContext)
    {
        switch (fixAllContext.Scope)
        {
            case FixAllScope.Document when fixAllContext.Document is not null:
                return await fixAllContext.GetDocumentDiagnosticsAsync(fixAllContext.Document).ConfigureAwait(false);
            case FixAllScope.Solution:
                var diagnostics = new List<Diagnostic>();
                foreach (var project in fixAllContext.Solution.Projects)
                    diagnostics.AddRange(await fixAllContext.GetAllDiagnosticsAsync(project).ConfigureAwait(false));
                return diagnostics;
            default:
                return await fixAllContext.GetAllDiagnosticsAsync(fixAllContext.Project).ConfigureAwait(false);
        }
    }

    private static FixAllContext WithEquivalenceKey(FixAllContext fixAllContext, string key)
    {
        var diagnostics = new ForwardingDiagnosticProvider(fixAllContext);
        return fixAllContext.Document is { } document
            ? new FixAllContext(document, fixAllContext.CodeFixProvider, fixAllContext.Scope, key, fixAllContext.DiagnosticIds, diagnostics, fixAllContext.CancellationToken)
            : new FixAllContext(fixAllContext.Project, fixAllContext.CodeFixProvider, fixAllContext.Scope, key, fixAllContext.DiagnosticIds, diagnostics, fixAllContext.CancellationToken);
    }

    /// <summary>The original request's diagnostics, which a new context cannot take over directly.</summary>
    private sealed class ForwardingDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly FixAllContext _original;

        public ForwardingDiagnosticProvider(FixAllContext original) => _original = original;

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            await _original.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);

        public override async Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            await _original.GetProjectDiagnosticsAsync(project).ConfigureAwait(false);

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            await _original.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
    }
}
