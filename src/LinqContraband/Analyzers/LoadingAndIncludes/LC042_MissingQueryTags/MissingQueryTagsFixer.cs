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

namespace LinqContraband.Analyzers.LC042_MissingQueryTags;

/// <summary>
/// Provides code fixes for LC042. Inserts a tag right before the method that runs the query.
/// </summary>
/// <remarks>
/// Two fixes are offered: <c>TagWith("Type.Member")</c>, named after the member that contains the query, and
/// <c>TagWithCallSite()</c> when the referenced EF Core version has it (6.0+). Both return the same
/// <c>IQueryable&lt;T&gt;</c> the terminal already accepted. The static invocation form
/// (<c>Enumerable.ToList(query)</c>) is reported without a fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingQueryTagsFixer))]
[Shared]
public sealed class MissingQueryTagsFixer : CodeFixProvider
{
    private const string EfCoreNamespace = "Microsoft.EntityFrameworkCore";
    private const string TagWithKey = nameof(MissingQueryTagsFixer) + ".TagWith";
    private const string TagWithCallSiteKey = nameof(MissingQueryTagsFixer) + ".TagWithCallSite";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingQueryTagsAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        var extensions = semanticModel.Compilation.GetTypeByMetadataName(EfCoreNamespace + ".EntityFrameworkQueryableExtensions");
        if (extensions == null) return;

        var hasTagWith = extensions.GetMembers("TagWith").Any();
        var hasTagWithCallSite = extensions.GetMembers("TagWithCallSite").Any();

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is not SimpleNameSyntax { Parent: MemberAccessExpressionSyntax memberAccess } ||
                memberAccess.Name != node ||
                memberAccess.Parent is not InvocationExpressionSyntax terminal ||
                terminal.Expression != memberAccess)
            {
                continue;
            }

            // Only the fluent extension form (query.ToList()) has a receiver to tag; Enumerable.ToList(query) does not.
            if (semanticModel.GetSymbolInfo(terminal, context.CancellationToken).Symbol is not IMethodSymbol
                {
                    MethodKind: MethodKind.ReducedExtension
                })
            {
                continue;
            }

            if (hasTagWith && TryGetTagText(semanticModel, terminal, context.CancellationToken) is { } tagText)
            {
                var argument = SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(tagText)));
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Tag query with \"{tagText}\"",
                        ct => InsertTagAsync(context.Document, memberAccess, "TagWith", argument, ct),
                        TagWithKey),
                    diagnostic);
            }

            if (hasTagWithCallSite)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Tag query with its call site",
                        ct => InsertTagAsync(context.Document, memberAccess, "TagWithCallSite", null, ct),
                        TagWithCallSiteKey),
                    diagnostic);
            }
        }
    }

    private static async Task<Document> InsertTagAsync(
        Document document,
        MemberAccessExpressionSyntax memberAccess,
        string tagMethod,
        ArgumentSyntax? argument,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var receiver = memberAccess.Expression;

        // Keep the chain's layout: in a multi-line chain the tag gets its own line with the terminal's indentation.
        var dot = SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(memberAccess.OperatorToken.LeadingTrivia);
        var tagAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            dot,
            SyntaxFactory.IdentifierName(tagMethod));
        var arguments = argument == null
            ? SyntaxFactory.ArgumentList()
            : SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument));
        var tagCall = SyntaxFactory.InvocationExpression(tagAccess, arguments)
            .WithTrailingTrivia(receiver.GetTrailingTrivia());

        editor.ReplaceNode(memberAccess, memberAccess.WithExpression(tagCall));

        if (!IsTagMethodInScope(editor.SemanticModel, memberAccess, tagMethod))
            editor.EnsureUsing(EfCoreNamespace);

        return editor.GetChangedDocument();
    }

    /// <summary>
    /// True when the EF Core extension methods already bind at this position, through a using in scope, a global
    /// using, or the code living in the EF Core namespace itself.
    /// </summary>
    private static bool IsTagMethodInScope(SemanticModel semanticModel, MemberAccessExpressionSyntax memberAccess, string tagMethod)
    {
        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType == null)
            return false;

        return semanticModel
            .LookupSymbols(memberAccess.SpanStart, receiverType, tagMethod, includeReducedExtensionMethods: true)
            .Any();
    }

    private static string? TryGetTagText(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        while (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
            symbol = symbol.ContainingSymbol;

        if (symbol?.ContainingType is not { } containingType || containingType.IsImplicitlyDeclared)
            return null;

        var memberName = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => null,
            IMethodSymbol { AssociatedSymbol: { } associated } => associated.Name,
            IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.Name,
            IPropertySymbol or IFieldSymbol => symbol.Name,
            _ => null
        };

        return memberName == null ? containingType.Name : containingType.Name + "." + memberName;
    }
}
