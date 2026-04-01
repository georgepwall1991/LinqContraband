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

namespace LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidExecuteSqlRawWithInterpolationFixer))]
[Shared]
public sealed class AvoidExecuteSqlRawWithInterpolationFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AvoidExecuteSqlRawWithInterpolationAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        if (token.Parent is null)
            return;

        var invocation = token.Parent.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
            return;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var replacementName = memberAccess.Name.Identifier.Text switch
        {
            "ExecuteSqlRaw" => "ExecuteSql",
            "ExecuteSqlRawAsync" => "ExecuteSqlAsync",
            _ => null
        };

        if (replacementName is null)
            return;

        var firstArg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (firstArg?.Expression is not InterpolatedStringExpressionSyntax)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Replace with {replacementName}",
                cancellationToken => ApplyFixAsync(context.Document, memberAccess, replacementName, cancellationToken),
                replacementName),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        MemberAccessExpressionSyntax memberAccess,
        string replacementName,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(memberAccess, memberAccess.WithName(SyntaxFactory.IdentifierName(replacementName)));
        return editor.GetChangedDocument();
    }
}
