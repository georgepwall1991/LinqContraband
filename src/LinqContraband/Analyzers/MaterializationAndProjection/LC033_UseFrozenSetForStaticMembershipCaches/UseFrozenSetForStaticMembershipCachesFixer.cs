using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC033_UseFrozenSetForStaticMembershipCaches;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseFrozenSetForStaticMembershipCachesFixer))]
[Shared]
public sealed partial class UseFrozenSetForStaticMembershipCachesFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UseFrozenSetForStaticMembershipCachesAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(UseFrozenSetForStaticMembershipCachesDiagnosticProperties.FixerEligible, out var fixerEligible) ||
            fixerEligible != "true")
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var fieldDeclaration = node as FieldDeclarationSyntax ??
                               node.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        if (fieldDeclaration?.Declaration.Variables.Count != 1)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Convert to FrozenSet",
                cancellationToken => ApplyFixAsync(context.Document, fieldDeclaration, cancellationToken),
                nameof(UseFrozenSetForStaticMembershipCachesFixer)),
            diagnostic);
    }
}
