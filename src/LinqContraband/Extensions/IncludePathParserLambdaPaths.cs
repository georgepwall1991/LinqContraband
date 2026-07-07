using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

internal static partial class IncludePathParser
{
    private static readonly ImmutableHashSet<string> FilteredIncludeMethods = ImmutableHashSet.Create(
        System.StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take");

    private static bool TryGetLambdaExpression(
        IInvocationOperation invocation,
        out LambdaExpressionSyntax lambdaExpression)
    {
        lambdaExpression = null!;
        if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax ||
            invocationSyntax.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var expression = invocationSyntax.ArgumentList.Arguments[invocationSyntax.ArgumentList.Arguments.Count - 1].Expression;
        if (expression is LambdaExpressionSyntax lambda)
        {
            lambdaExpression = lambda;
            return true;
        }

        return false;
    }

    private static bool TryGetNavigationPath(
        CSharpSyntaxNode body,
        SemanticModel semanticModel,
        out IncludePath includePath)
    {
        includePath = new IncludePath(ImmutableArray<NavigationSegment>.Empty);
        var builder = ImmutableArray.CreateBuilder<NavigationSegment>();

        if (!TryAddNavigationSegments(body, semanticModel, builder))
            return false;

        includePath = new IncludePath(builder.ToImmutable());
        return includePath.Segments.Length > 0;
    }

    private static bool TryAddNavigationSegments(
        CSharpSyntaxNode expression,
        SemanticModel semanticModel,
        ImmutableArray<NavigationSegment>.Builder builder)
    {
        expression = UnwrapExpression(expression);

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax invocationMemberAccess &&
            FilteredIncludeMethods.Contains(invocationMemberAccess.Name.Identifier.Text))
        {
            return TryAddNavigationSegments(invocationMemberAccess.Expression, semanticModel, builder);
        }

        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        // Unwrap mid-path casts/parens/null-forgiving (`o.Customer!.Address`,
        // `((Derived)o.Nav).Child`) so the parent segments are still collected. Any other
        // parent shape fails the parse; truncating would turn an unknown into a wrong path.
        switch (UnwrapExpression(memberAccess.Expression))
        {
            case MemberAccessExpressionSyntax parentMemberAccess:
                if (!TryAddNavigationSegments(parentMemberAccess, semanticModel, builder))
                    return false;
                break;
            case InvocationExpressionSyntax parentInvocation:
                if (!TryAddNavigationSegments(parentInvocation, semanticModel, builder))
                    return false;
                break;
            case IdentifierNameSyntax:
                break;
            default:
                return false;
        }

        var property = semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (property == null)
            return false;

        builder.Add(new NavigationSegment(property.Name, IsCollection(property.Type)));
        return true;
    }

    private static CSharpSyntaxNode UnwrapExpression(CSharpSyntaxNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression:
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }
}
