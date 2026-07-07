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
/// Provides code fixes for LC018. Replaces raw SQL query APIs with interpolated counterparts when safe.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AvoidFromSqlRawWithInterpolationFixer))]
[Shared]
public sealed partial class AvoidFromSqlRawWithInterpolationFixer : CodeFixProvider
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var replacementName = GetReplacementName(memberAccess.Name.Identifier.Text);
        if (replacementName is null)
            return;

        var sqlArgument = GetSqlArgument(invocation);
        if (sqlArgument?.Expression is not InterpolatedStringExpressionSyntax interpolatedSql)
            return;

        if (HasInterpolationInsideSqlStringLiteral(interpolatedSql) ||
            HasInterpolationOutsideLikelySqlValuePosition(interpolatedSql))
            return;

        if (invocation.ArgumentList.Arguments.Count != 1)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Replace with {replacementName}",
                c => ApplyFixAsync(context.Document, memberAccess, replacementName, c),
                GetEquivalenceKey(memberAccess.Name.Identifier.Text)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, MemberAccessExpressionSyntax memberAccess, string replacementName, CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(memberAccess, memberAccess.WithName(WithReplacementName(memberAccess.Name, replacementName)));
        return editor.GetChangedDocument();
    }

    private static SimpleNameSyntax WithReplacementName(SimpleNameSyntax name, string replacementName)
    {
        return name is GenericNameSyntax genericName
            ? genericName.WithIdentifier(SyntaxFactory.Identifier(replacementName))
            : SyntaxFactory.IdentifierName(replacementName);
    }

    private static string? GetReplacementName(string methodName)
    {
        return methodName switch
        {
            "FromSqlRaw" => "FromSqlInterpolated",
            "SqlQueryRaw" => "SqlQuery",
            _ => null
        };
    }

    private static string GetEquivalenceKey(string methodName)
    {
        return methodName switch
        {
            "SqlQueryRaw" => "ReplaceSqlQueryRawWithSqlQuery",
            _ => "ReplaceFromSqlRawWithInterpolated"
        };
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

}
