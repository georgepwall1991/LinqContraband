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
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC057_EmptyQueryAggregate;

/// <summary>
/// Provides code fixes for LC057. Casts the aggregated value to its nullable type (<c>Max(x =&gt; (decimal?)x.Price)</c>,
/// or a casting selector for the selector-less overloads) so an empty query returns <c>null</c>, and appends
/// <c>?? default</c> when the result is used as the non-nullable value it was before.
/// </summary>
/// <remarks>
/// The fixer compiles the rewritten document and only offers a rewrite that adds no errors and leaves the result
/// with the type it had. A block-bodied lambda, a selector that is not a lambda, and an async aggregate whose task is
/// not awaited where it is created get no fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EmptyQueryAggregateFixer))]
[Shared]
public sealed class EmptyQueryAggregateFixer : CodeFixProvider
{
    private static readonly string[] ParameterNames = { "x", "value", "v", "item" };

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EmptyQueryAggregateAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => LinqContrabandFixAllProvider.Instance;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var cancellationToken = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root == null || semanticModel == null) return;

        int? errorsBefore = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not SimpleNameSyntax
                {
                    Parent: MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocationSyntax } memberAccess
                } ||
                invocationSyntax.Expression != memberAccess ||
                semanticModel.GetOperation(invocationSyntax, cancellationToken) is not IInvocationOperation invocation ||
                !EmptyQueryAggregateAnalyzer.TryGetAggregatedValueType(invocation, out var valueType))
            {
                continue;
            }

            var nullableType = SyntaxFactory.NullableType(
                SyntaxFactory.ParseTypeName(valueType.ToMinimalDisplayString(semanticModel, invocationSyntax.SpanStart)));

            var newInvocation = RewriteInvocation(invocation, invocationSyntax, nullableType, semanticModel);
            if (newInvocation == null) continue;

            var resultNode = GetResultNode(invocationSyntax, invocation.TargetMethod.Name);
            if (resultNode == null) continue;

            var typeInfo = semanticModel.GetTypeInfo(resultNode, cancellationToken);
            if (typeInfo.Type == null || typeInfo.ConvertedType == null) continue;

            var discarded = resultNode.Parent is ExpressionStatementSyntax;
            var alreadyNullable = typeInfo.ConvertedType is INamedTypeSymbol
                                  {
                                      OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                                  } converted &&
                                  SymbolEqualityComparer.Default.Equals(converted.TypeArguments[0], typeInfo.Type);

            ExpressionSyntax newResult = resultNode == invocationSyntax
                ? newInvocation
                : resultNode.ReplaceNode(invocationSyntax, newInvocation);

            if (!discarded && !alreadyNullable)
            {
                ExpressionSyntax coalesce = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    newResult.WithoutTrivia(),
                    SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression));
                if (NeedsParentheses(resultNode))
                    coalesce = SyntaxFactory.ParenthesizedExpression(coalesce);

                newResult = coalesce.WithTriviaFrom(resultNode);
            }

            var annotation = new SyntaxAnnotation();
            var newRoot = root.ReplaceNode(resultNode, newResult.WithAdditionalAnnotations(annotation));
            var newDocument = document.WithSyntaxRoot(newRoot);

            errorsBefore ??= CountErrors(semanticModel, cancellationToken);
            if (!await KeepsTypeAndCompilesAsync(newDocument, annotation, typeInfo, discarded, errorsBefore.Value, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Cast the value to '{nullableType}'",
                    _ => Task.FromResult(newDocument),
                    nameof(EmptyQueryAggregateFixer)),
                diagnostic);
        }
    }

    private static InvocationExpressionSyntax? RewriteInvocation(
        IInvocationOperation invocation,
        InvocationExpressionSyntax invocationSyntax,
        NullableTypeSyntax nullableType,
        SemanticModel semanticModel)
    {
        var selector = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "selector");
        if (selector != null)
        {
            if (selector.Syntax is not ArgumentSyntax { Expression: LambdaExpressionSyntax lambda } ||
                lambda.ExpressionBody is not { } body)
            {
                return null;
            }

            var cast = SyntaxFactory.CastExpression(nullableType, Parenthesize(body.WithoutTrivia())).WithTriviaFrom(body);
            return invocationSyntax.ReplaceNode(body, cast);
        }

        var name = ParameterNames.FirstOrDefault(candidate =>
            semanticModel.LookupSymbols(invocationSyntax.SpanStart, name: candidate).IsEmpty);
        if (name == null) return null;

        var newSelector = SyntaxFactory.Argument(
            SyntaxFactory.SimpleLambdaExpression(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(name)),
                SyntaxFactory.CastExpression(nullableType, SyntaxFactory.IdentifierName(name))));

        // A static call (Queryable.Max(source)) passes the source as its first argument.
        var arguments = invocationSyntax.ArgumentList.Arguments;
        var index = invocation.Arguments.Any(argument =>
            argument.Parameter?.Ordinal == 0 && arguments.Any(syntax => syntax == argument.Syntax))
            ? 1
            : 0;
        if (index > arguments.Count) return null;

        return invocationSyntax.WithArgumentList(
            invocationSyntax.ArgumentList.WithArguments(arguments.Insert(index, newSelector)));
    }

    private static ExpressionSyntax Parenthesize(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or
            ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax or
            CastExpressionSyntax or ThisExpressionSyntax
            ? expression
            : SyntaxFactory.ParenthesizedExpression(expression);
    }

    /// <summary>
    /// The expression whose value is the aggregate's result: the call itself, or for an async aggregate the
    /// <c>await</c> of it (directly or through <c>ConfigureAwait</c>). An async aggregate whose task is used any other
    /// way has no result expression, so it gets no fix.
    /// </summary>
    private static ExpressionSyntax? GetResultNode(InvocationExpressionSyntax invocationSyntax, string methodName)
    {
        if (!methodName.EndsWith("Async", System.StringComparison.Ordinal))
            return invocationSyntax;

        ExpressionSyntax task = invocationSyntax;
        if (task.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configureAwait &&
            configureAwait.Expression == task &&
            configureAwait.Parent is InvocationExpressionSyntax configureAwaitCall)
        {
            task = configureAwaitCall;
        }

        return task.Parent is AwaitExpressionSyntax awaitExpression ? awaitExpression : null;
    }

    private static bool NeedsParentheses(ExpressionSyntax resultNode)
    {
        return resultNode.Parent switch
        {
            EqualsValueClauseSyntax => false,
            ArgumentSyntax => false,
            ReturnStatementSyntax => false,
            ArrowExpressionClauseSyntax => false,
            ParenthesizedExpressionSyntax => false,
            LambdaExpressionSyntax lambda when lambda.ExpressionBody == resultNode => false,
            AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                                       assignment.Right == resultNode => false,
            _ => true
        };
    }

    private static async Task<bool> KeepsTypeAndCompilesAsync(
        Document newDocument,
        SyntaxAnnotation annotation,
        TypeInfo original,
        bool discarded,
        int errorsBefore,
        CancellationToken cancellationToken)
    {
        var newRoot = await newDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newModel = await newDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (newRoot == null || newModel == null || CountErrors(newModel, cancellationToken) > errorsBefore)
            return false;

        if (discarded)
            return true;

        var newNode = newRoot.GetAnnotatedNodes(annotation).FirstOrDefault();
        if (newNode is not ExpressionSyntax newExpression)
            return false;

        var newInfo = newModel.GetTypeInfo(newExpression, cancellationToken);
        return SameType(newInfo.ConvertedType, original.ConvertedType) &&
               (SameType(newInfo.Type, original.Type) || SameType(newInfo.Type, original.ConvertedType));
    }

    private static bool SameType(ITypeSymbol? left, ITypeSymbol? right)
    {
        return left != null && right != null &&
               left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static int CountErrors(SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
