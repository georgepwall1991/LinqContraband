using System.Collections.Immutable;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC006_CartesianExplosion;

public sealed partial class CartesianExplosionAnalyzer
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

    private static bool TryGetIncludePath(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        IncludePath? previousIncludePath,
        out IncludePath includePath)
    {
        includePath = new IncludePath(ImmutableArray<NavigationSegment>.Empty);

        if (invocation.TargetMethod.Name == "Include" &&
            TryGetStringIncludePath(invocation, out includePath))
        {
            return true;
        }

        if (!TryGetLambdaExpression(invocation, out var lambdaExpression))
            return false;

        if (!TryGetNavigationPath(lambdaExpression.Body, semanticModel, out var lambdaPath))
            return false;

        if (invocation.TargetMethod.Name == "ThenInclude")
        {
            if (previousIncludePath == null)
                return false;

            includePath = previousIncludePath.Append(lambdaPath);
            return true;
        }

        includePath = lambdaPath;
        return true;
    }

    private static bool TryGetStringIncludePath(IInvocationOperation invocation, out IncludePath includePath)
    {
        includePath = new IncludePath(ImmutableArray<NavigationSegment>.Empty);
        if (invocation.Arguments.Length == 0)
            return false;

        var value = invocation.Arguments[invocation.Arguments.Length - 1].Value;
        if (!value.ConstantValue.HasValue || value.ConstantValue.Value is not string pathText)
            return false;

        if (string.IsNullOrWhiteSpace(pathText))
            return false;

        var receiverType = invocation.GetInvocationReceiverType();
        if (!TryGetQueryableElementType(receiverType, out var currentType))
            return false;

        var builder = ImmutableArray.CreateBuilder<NavigationSegment>();
        foreach (var rawSegment in pathText.Split('.'))
        {
            var segmentName = rawSegment.Trim();
            if (segmentName.Length == 0)
                return false;

            var property = TryFindProperty(currentType, segmentName);
            if (property == null)
                return false;

            var propertyType = property.Type;
            var isCollection = IsCollection(propertyType);
            builder.Add(new NavigationSegment(property.Name, isCollection));

            if (isCollection)
            {
                if (!TryGetCollectionElementType(propertyType, out currentType))
                    return false;
            }
            else
            {
                currentType = propertyType;
            }
        }

        includePath = new IncludePath(builder.ToImmutable());
        return true;
    }

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

        if (memberAccess.Expression is MemberAccessExpressionSyntax parentMemberAccess)
        {
            if (!TryAddNavigationSegments(parentMemberAccess, semanticModel, builder))
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
                case PostfixUnaryExpressionSyntax postfix when postfix.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression:
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static IPropertySymbol? TryFindProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol property)
                    return property;
            }
        }

        return null;
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;
        if (type.TypeKind == TypeKind.Array)
            return true;

        return TryGetCollectionElementType(type, out _);
    }

    private static bool TryGetQueryableElementType(ITypeSymbol? type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type == null)
            return false;

        if (TryGetNamedGenericElementType(type, "IQueryable", "System.Linq", out elementType))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (TryGetNamedGenericElementType(iface, "IQueryable", "System.Linq", out elementType))
                return true;
        }

        return false;
    }

    private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type.SpecialType == SpecialType.System_String)
            return false;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (TryGetNamedGenericElementType(type, "IEnumerable", "System.Collections.Generic", out elementType))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (TryGetNamedGenericElementType(iface, "IEnumerable", "System.Collections.Generic", out elementType))
                return true;
        }

        return false;
    }

    private static bool TryGetNamedGenericElementType(
        ITypeSymbol type,
        string typeName,
        string namespaceName,
        out ITypeSymbol elementType)
    {
        elementType = null!;
        if (type is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.Name != typeName ||
            namedType.ContainingNamespace?.ToString() != namespaceName ||
            namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = namedType.TypeArguments[0];
        return true;
    }
}
