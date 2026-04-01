using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

/// <summary>
/// Provides code fixes for LC018. Replaces FromSqlRaw with FromSqlInterpolated when interpolated strings are used.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidFromSqlRawWithInterpolationFixer))]
[Shared]
public class AvoidFromSqlRawWithInterpolationFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidFromSqlRawWithInterpolationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var token = root.FindToken(diagnosticSpan.Start);
        if (token.Parent is null) return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null) return;

        // Ensure it's actually FromSqlRaw
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "FromSqlRaw")
        {
            // Only offer fix if the first argument is an interpolated string.
            // (Concatenations are harder to fix automatically)
            var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
            if (firstArg?.Expression is InterpolatedStringExpressionSyntax)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Replace with FromSqlInterpolated",
                        c => ApplyFixAsync(context.Document, invocation, c),
                        "ReplaceFromSqlRawWithInterpolated"),
                    diagnostic);
            }
        }
    }

    private async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var newName = SyntaxFactory.IdentifierName("FromSqlInterpolated");
            var newMemberAccess = memberAccess.WithName(newName);
            editor.ReplaceNode(memberAccess, newMemberAccess);
        }

        return editor.GetChangedDocument();
    }
}
