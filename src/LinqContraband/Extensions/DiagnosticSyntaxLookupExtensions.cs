using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LinqContraband.Extensions;

/// <summary>
/// Syntax lookups shared by code-fix providers.
/// </summary>
internal static class DiagnosticSyntaxLookupExtensions
{
    /// <summary>
    /// Returns the invocation a diagnostic was reported on, i.e. the invocation whose span is
    /// exactly <paramref name="diagnosticSpan"/>.
    /// <para>
    /// Walking up from <c>FindToken(diagnosticSpan.Start)</c> to the first invocation is wrong
    /// for a chained receiver: in <c>db.Users.AsAsyncEnumerable().ToListAsync()</c> the first
    /// token is <c>db</c>, and its nearest invocation ancestor is the inner
    /// <c>db.Users.AsAsyncEnumerable()</c> call, not the reported one.
    /// </para>
    /// When no invocation matches the span exactly, this falls back to the nearest invocation
    /// enclosing the whole span.
    /// </summary>
    public static InvocationExpressionSyntax? FindReportedInvocation(this SyntaxNode root, TextSpan diagnosticSpan)
    {
        var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
        var invocations = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>();
        return invocations.FirstOrDefault(invocation => invocation.Span == diagnosticSpan)
               ?? invocations.FirstOrDefault();
    }
}
