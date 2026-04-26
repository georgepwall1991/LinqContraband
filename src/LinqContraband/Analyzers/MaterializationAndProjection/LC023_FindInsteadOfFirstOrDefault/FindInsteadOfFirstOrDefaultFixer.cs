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
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

/// <summary>
/// Provides code fixes for LC023. Replaces FirstOrDefault/SingleOrDefault primary key lookups with Find/FindAsync.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FindInsteadOfFirstOrDefaultFixer))]
[Shared]
public class FindInsteadOfFirstOrDefaultFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FindInsteadOfFirstOrDefaultAnalyzer.DiagnosticId);

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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        if (!TryCreateFixContext(invocation, semanticModel, context.CancellationToken, out var fixContext))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use Find/FindAsync",
                c => ApplyFixAsync(context.Document, invocation, fixContext, c),
                "UseFind"),
            diagnostic);
    }

    private static bool TryCreateFixContext(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out FixContext fixContext)
    {
        fixContext = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName is not ("FirstOrDefault" or "SingleOrDefault" or "FirstOrDefaultAsync" or "SingleOrDefaultAsync"))
            return false;

        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation)
            return false;

        if (!operation.GetInvocationReceiverType().IsDbSet())
            return false;

        var isAsync = methodName.EndsWith("Async");
        if (isAsync && !IsAwaited(invocation))
            return false;

        var predicateArgument = operation.Arguments
            .FirstOrDefault(argument => argument.Value.UnwrapConversions() is IAnonymousFunctionOperation);
        if (predicateArgument == null || predicateArgument.Syntax is not ArgumentSyntax predicateSyntax)
            return false;

        if (predicateSyntax.Expression is not LambdaExpressionSyntax lambda ||
            lambda.Body is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.EqualsExpression))
        {
            return false;
        }

        if (!TryGetKeyValueExpression(binary, semanticModel, cancellationToken, out var valueExpression))
            return false;

        var cancellationTokenArgument = operation.Arguments
            .FirstOrDefault(argument => IsCancellationTokenParameter(argument.Parameter));

        var tokenSyntax = cancellationTokenArgument?.Syntax as ArgumentSyntax;
        fixContext = new FixContext(methodName, valueExpression, tokenSyntax);
        return true;
    }

    private async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        FixContext fixContext,
        CancellationToken cancellationToken)
    {
        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return document;

        var newMethodName = fixContext.IsAsync ? "FindAsync" : "Find";
        var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(newMethodName));
        var newArguments = CreateFindArgumentList(fixContext);

        var newInvocation = SyntaxFactory.InvocationExpression(newMemberAccess, newArguments)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        editor.ReplaceNode(invocation, newInvocation);
        return editor.GetChangedDocument();
    }

    private static bool TryGetKeyValueExpression(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        if (IsPrimaryKeyAccess(binary.Left, semanticModel, cancellationToken))
        {
            valueExpression = binary.Right.WithoutTrivia();
            return true;
        }

        if (IsPrimaryKeyAccess(binary.Right, semanticModel, cancellationToken))
        {
            valueExpression = binary.Left.WithoutTrivia();
            return true;
        }

        valueExpression = null!;
        return false;
    }

    private static bool IsPrimaryKeyAccess(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var operation = semanticModel.GetOperation(expression, cancellationToken)?.UnwrapConversions();
        if (operation is not IPropertyReferenceOperation propertyReference)
            return false;

        return propertyReference.Instance?.UnwrapConversions() is IParameterReferenceOperation &&
               FindInsteadOfFirstOrDefaultKeyAnalysis.TryFindSafePrimaryKey(
                   propertyReference.Property.ContainingType,
                   semanticModel.Compilation,
                   cancellationToken) == propertyReference.Property.Name;
    }

    private static ArgumentListSyntax CreateFindArgumentList(FixContext fixContext)
    {
        if (!fixContext.IsAsync || fixContext.CancellationTokenArgument == null)
        {
            return SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(fixContext.KeyValueExpression)));
        }

        var keyArray = SyntaxFactory.ArrayCreationExpression(
            SyntaxFactory.ArrayType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                SyntaxFactory.SingletonList(
                    SyntaxFactory.ArrayRankSpecifier(
                        SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                            SyntaxFactory.OmittedArraySizeExpression())))),
            SyntaxFactory.InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SyntaxFactory.SingletonSeparatedList(fixContext.KeyValueExpression)));

        return SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(keyArray),
                fixContext.CancellationTokenArgument.WithoutTrivia()
            }));
    }

    private static bool IsAwaited(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is AwaitExpressionSyntax)
                return true;

            if (current is ParenthesizedExpressionSyntax)
                continue;

            return false;
        }

        return false;
    }

    private static bool IsCancellationTokenParameter(IParameterSymbol? parameter)
    {
        return parameter?.Type.Name == nameof(CancellationToken) &&
               parameter.Type.ContainingNamespace?.ToString() == "System.Threading";
    }

    private sealed class FixContext
    {
        public FixContext(string methodName, ExpressionSyntax keyValueExpression, ArgumentSyntax? cancellationTokenArgument)
        {
            IsAsync = methodName.EndsWith("Async");
            KeyValueExpression = keyValueExpression;
            CancellationTokenArgument = cancellationTokenArgument;
        }

        public bool IsAsync { get; }

        public ExpressionSyntax KeyValueExpression { get; }

        public ArgumentSyntax? CancellationTokenArgument { get; }
    }
}
