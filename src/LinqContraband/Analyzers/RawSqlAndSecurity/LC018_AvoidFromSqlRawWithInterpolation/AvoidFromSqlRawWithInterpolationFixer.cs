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

    public sealed override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

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

        var sqlArgument = GetSqlArgument(invocation);
        if (sqlArgument?.Expression is not InterpolatedStringExpressionSyntax interpolatedSql)
            return;

        if (HasInterpolationInsideSqlStringLiteral(interpolatedSql) ||
            HasInterpolationOutsideLikelySqlValuePosition(interpolatedSql))
            return;

        if (invocation.ArgumentList.Arguments.Count != 1)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        var replacementName = GetReplacementName(invocation, memberAccess, semanticModel, context.CancellationToken);
        if (replacementName is null)
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

    /// <summary>
    /// Picks the parameterizing API the rewritten call actually binds to: <c>FromSql</c> first (EF Core 7+,
    /// and the only interpolated overload on Cosmos), then <c>FromSqlInterpolated</c> for older EF Core.
    /// A name is used only when the rewritten call binds to a non-obsolete overload taking a
    /// <c>FormattableString</c>, so the fix never emits a call that fails to compile or trips CS0618
    /// (EF Core 11 marks <c>FromSqlInterpolated</c> obsolete).
    /// </summary>
    private static string? GetReplacementName(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var candidates = memberAccess.Name.Identifier.Text switch
        {
            "FromSqlRaw" => new[] { "FromSql", "FromSqlInterpolated" },
            "SqlQueryRaw" => new[] { "SqlQuery" },
            _ => null
        };

        if (candidates is null)
            return null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rewritten = invocation.WithExpression(memberAccess.WithName(WithReplacementName(memberAccess.Name, candidate)));
            var symbol = semanticModel.GetSpeculativeSymbolInfo(invocation.SpanStart, rewritten, SpeculativeBindingOption.BindAsExpression).Symbol;
            if (symbol is IMethodSymbol method &&
                method.Name == candidate &&
                TakesFormattableSql(method) &&
                !(method.ReducedFrom ?? method).IsObsolete())
                return candidate;
        }

        return null;
    }

    private static bool TakesFormattableSql(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.Name == "FormattableString" &&
                parameter.Type.ContainingNamespace?.ToDisplayString() == "System")
                return true;
        }

        return false;
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
