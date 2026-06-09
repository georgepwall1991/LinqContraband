using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LinqContraband.Extensions;

/// <summary>
/// Trivia helpers shared by code-fix providers.
/// </summary>
internal static class SyntaxTriviaExtensions
{
    /// <summary>
    /// Returns an end-of-line trivia matching the document's existing line endings
    /// (CRLF on Windows checkouts, LF on Unix), harvested from <paramref name="node"/>
    /// or, failing that, from the syntax tree root.
    /// <para>
    /// Fixers that insert or move a statement should terminate it with this rather than a
    /// hard-coded <see cref="SyntaxFactory.ElasticLineFeed"/> or <c>EndOfLine("\n")</c>:
    /// those emit a lone LF, which leaves a single LF line inside an otherwise CRLF file
    /// (mixed line endings that show up as a spurious source-control diff and can trip
    /// formatters/linters) and makes fixer tests fail on CRLF checkouts.
    /// </para>
    /// </summary>
    public static SyntaxTrivia GetDocumentEndOfLine(this SyntaxNode node)
    {
        var local = FirstEndOfLine(node);
        if (local.IsKind(SyntaxKind.EndOfLineTrivia))
            return local;

        var root = node.SyntaxTree?.GetRoot();
        if (root is not null)
        {
            var fromRoot = FirstEndOfLine(root);
            if (fromRoot.IsKind(SyntaxKind.EndOfLineTrivia))
                return fromRoot;
        }

        return SyntaxFactory.ElasticLineFeed;
    }

    private static SyntaxTrivia FirstEndOfLine(SyntaxNode node) =>
        node.DescendantTrivia().FirstOrDefault(static trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
}
