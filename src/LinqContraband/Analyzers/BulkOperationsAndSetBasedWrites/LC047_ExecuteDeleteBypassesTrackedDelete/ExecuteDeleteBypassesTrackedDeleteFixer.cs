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

namespace LinqContraband.Analyzers.LC047_ExecuteDeleteBypassesTrackedDelete;

/// <summary>
/// Rewrites a proven soft-delete <c>ExecuteDelete</c> into <c>ExecuteUpdate</c> that sets the
/// converted bool property. Client-cascade and multi-property conversions stay diagnostic-only.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExecuteDeleteBypassesTrackedDeleteFixer))]
[Shared]
public sealed class ExecuteDeleteBypassesTrackedDeleteFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExecuteDeleteBypassesTrackedDeleteAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(
                ExecuteDeleteBypassesTrackedDeleteAnalyzer.ConversionPropertyKey,
                out var propertyName) ||
            string.IsNullOrEmpty(propertyName))
        {
            return;
        }

        var conversionProperty = propertyName!;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use ExecuteUpdate to set the converted delete property",
                cancellationToken => ApplyFixAsync(context.Document, invocation, conversionProperty, cancellationToken),
                "UseExecuteUpdateForSoftDelete"),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var symbol = semanticModel?.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        var rewritten = RewriteInvocation(invocation, propertyName, symbol?.IsExtensionMethod == true);
        editor.ReplaceNode(invocation, rewritten);
        editor.EnsureUsing("Microsoft.EntityFrameworkCore");
        return editor.GetChangedDocument();
    }

    private static InvocationExpressionSyntax RewriteInvocation(
        InvocationExpressionSyntax invocation,
        string propertyName,
        bool isExtensionMethod)
    {
        var setter = SyntaxFactory.ParseExpression(
            $"setters => setters.SetProperty(e => e.{propertyName}, true)");
        var setterArgument = SyntaxFactory.Argument(setter);

        var newExpression = RenameExecuteDelete(invocation.Expression);
        var newArguments = isExtensionMethod
            ? invocation.ArgumentList.Arguments.Insert(0, setterArgument)
            : InsertAfterSourceArgument(invocation.ArgumentList.Arguments, setterArgument);

        return invocation
            .WithExpression(newExpression)
            .WithArgumentList(
                invocation.ArgumentList.WithArguments(newArguments));
    }

    private static ExpressionSyntax RenameExecuteDelete(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.WithIdentifier(
                SyntaxFactory.Identifier(AsyncName(identifier.Identifier.ValueText))),
            GenericNameSyntax generic => generic.WithIdentifier(
                SyntaxFactory.Identifier(AsyncName(generic.Identifier.ValueText))),
            MemberAccessExpressionSyntax member => member.WithName(
                (SimpleNameSyntax)RenameExecuteDelete(member.Name)),
            MemberBindingExpressionSyntax binding => binding.WithName(
                (SimpleNameSyntax)RenameExecuteDelete(binding.Name)),
            _ => expression
        };
    }

    private static string AsyncName(string current)
    {
        return current switch
        {
            "ExecuteDeleteAsync" => "ExecuteUpdateAsync",
            "ExecuteDelete" => "ExecuteUpdate",
            _ => current
        };
    }

    private static SeparatedSyntaxList<ArgumentSyntax> InsertAfterSourceArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ArgumentSyntax setterArgument)
    {
        if (arguments.Count == 0)
            return SyntaxFactory.SingletonSeparatedList(setterArgument);

        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon?.Name.Identifier.ValueText == "source")
                return arguments.Insert(i + 1, setterArgument);
        }

        return arguments.Insert(1, setterArgument);
    }
}
