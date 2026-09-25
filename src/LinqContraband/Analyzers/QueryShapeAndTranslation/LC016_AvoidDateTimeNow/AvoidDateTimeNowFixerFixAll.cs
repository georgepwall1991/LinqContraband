using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    // Marks the reported reads during fix-all. A hoisted initializer drops it, so the read it moved is not fixed twice.
    private const string FixAllAnnotationKind = "LinqContraband.LC016.FixAll";

    private static MemberAccessExpressionSyntax? FindReportedClockAccess(SyntaxNode root, Diagnostic diagnostic)
    {
        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        return token.Parent?.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
    }

    private static StatementSyntax? FindHoistTarget(MemberAccessExpressionSyntax memberAccess) =>
        memberAccess.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();

    // A loop re-evaluates its condition and incrementors on every pass. Hoisting a clock read out of them
    // would freeze the time the loop keeps checking, as in `while (db.Jobs.Any(j => j.DueAt > DateTime.UtcNow))`.
    private static bool ReadsClockOnEveryIteration(MemberAccessExpressionSyntax memberAccess, StatementSyntax target)
    {
        return target switch
        {
            WhileStatementSyntax whileStatement => whileStatement.Condition.Span.Contains(memberAccess.Span),
            DoStatementSyntax doStatement => doStatement.Condition.Span.Contains(memberAccess.Span),
            ForStatementSyntax forStatement =>
                forStatement.Condition?.Span.Contains(memberAccess.Span) == true ||
                forStatement.Incrementors.Any(incrementor => incrementor.Span.Contains(memberAccess.Span)),
            _ => false
        };
    }

    private async Task<Document?> FixAllInDocumentAsync(
        Document document,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var annotations = new List<SyntaxAnnotation>();
        var annotatedNodes = new Dictionary<SyntaxNode, SyntaxAnnotation>();
        foreach (var diagnostic in diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start))
        {
            var access = FindReportedClockAccess(root, diagnostic);
            if (access == null || annotatedNodes.ContainsKey(access)) continue;

            var annotation = new SyntaxAnnotation(FixAllAnnotationKind);
            annotatedNodes.Add(access, annotation);
            annotations.Add(annotation);
        }

        document = document.WithSyntaxRoot(root.ReplaceNodes(
            annotatedNodes.Keys,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(annotatedNodes[original])));

        foreach (var annotation in annotations)
        {
            var currentRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // An earlier fix may already have replaced this read along with the one it was fixing.
            if (currentRoot?.GetAnnotatedNodes(annotation).OfType<MemberAccessExpressionSyntax>().FirstOrDefault() is not { } access ||
                !CanApplyFix(access) ||
                FindHoistTarget(access) is { } target && ReadsClockOnEveryIteration(access, target))
            {
                continue;
            }

            document = await ApplyFixAsync(document, access, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }
}
