using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesFixer
{
    private const string WarningComment =
        "// Warning: ExecuteUpdate runs immediately and bypasses change tracking and entity callbacks.";

    private static async Task<Document> ApplyFixAsync(
        Document document,
        ForEachStatementSyntax forEach,
        RewritePlan plan,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

        // The original loop can compile on DbContext/DbSet instance members plus System.Linq;
        // the generated ExecuteUpdate(...) extension needs its defining namespace in scope.
        if (plan.ImportNamespace is { Length: > 0 } importNamespace)
            editor.EnsureUsing(importNamespace);

        var builder = new StringBuilder();
        if (plan.IsAsync)
            builder.Append("await ");

        builder.Append(plan.ReceiverText);
        builder.Append(plan.IsAsync ? ".ExecuteUpdateAsync(setters => setters" : ".ExecuteUpdate(setters => setters");

        foreach (var (left, right) in plan.Setters)
        {
            builder.Append(".SetProperty(");
            builder.Append(plan.IterationName);
            builder.Append(" => ");
            builder.Append(left);
            builder.Append(", ");
            builder.Append(plan.IterationName);
            builder.Append(" => ");
            builder.Append(right);
            builder.Append(')');
        }

        // Carry the cancellation token from the awaited SaveChangesAsync onto ExecuteUpdateAsync
        // (which is now the actual database call) so cancellation is not silently lost.
        if (plan.IsAsync && plan.CancellationTokenText is { Length: > 0 } token)
        {
            builder.Append(", ");
            builder.Append(token);
        }

        builder.Append(");");

        var warning = SyntaxFactory.Comment(WarningComment);
        var newStatement = SyntaxFactory.ParseStatement(builder.ToString())
            .WithLeadingTrivia(forEach.GetLeadingTrivia().Add(warning).Add(SyntaxFactory.ElasticLineFeed))
            .WithTrailingTrivia(forEach.GetTrailingTrivia());

        editor.ReplaceNode(forEach, newStatement);

        return editor.GetChangedDocument();
    }
}
