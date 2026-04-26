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

        var sqlArgument = GetSqlArgument(invocation);
        if (sqlArgument?.Expression is not InterpolatedStringExpressionSyntax interpolatedSql)
            return;

        if (HasInterpolationInsideSqlStringLiteral(interpolatedSql))
            return;

        if (invocation.ArgumentList.Arguments.Count != 1)
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

    private static ArgumentSyntax? GetSqlArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == "sql")
                return argument;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon is null);
    }

    private static bool HasInterpolationInsideSqlStringLiteral(InterpolatedStringExpressionSyntax interpolatedSql)
    {
        var insideSqlStringLiteral = false;
        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    insideSqlStringLiteral = ToggleSqlStringLiteralState(text.TextToken.ValueText, insideSqlStringLiteral);
                    break;
                case InterpolationSyntax when insideSqlStringLiteral:
                    return true;
            }
        }

        return false;
    }

    private static bool ToggleSqlStringLiteralState(string text, bool insideSqlStringLiteral)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\'')
                continue;

            if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                index++;
                continue;
            }

            insideSqlStringLiteral = !insideSqlStringLiteral;
        }

        return insideSqlStringLiteral;
    }
}
