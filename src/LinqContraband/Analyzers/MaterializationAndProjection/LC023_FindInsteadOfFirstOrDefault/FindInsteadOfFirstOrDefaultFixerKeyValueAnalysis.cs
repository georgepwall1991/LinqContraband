using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;

public sealed partial class FindInsteadOfFirstOrDefaultFixer
{
    private static bool TryGetKeyValueExpression(
        BinaryExpressionSyntax binary,
        IAnonymousFunctionOperation lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        if (TryGetPrimaryKeyProperty(binary.Left, semanticModel, cancellationToken, out var keyProperty))
            return TryGetFindArgument(binary.Right, keyProperty, lambda, semanticModel, cancellationToken, out valueExpression);

        if (TryGetPrimaryKeyProperty(binary.Right, semanticModel, cancellationToken, out keyProperty))
            return TryGetFindArgument(binary.Left, keyProperty, lambda, semanticModel, cancellationToken, out valueExpression);

        valueExpression = null!;
        return false;
    }

    // Find checks the key value's run-time type against the key property and throws ArgumentException on a
    // mismatch, so `t.Id == id` with a `long` key and an `int` id cannot become `Find(id)`. A widening numeric
    // value gets a cast; anything else (nullable values, user-defined conversions) keeps the original query.
    private static bool TryGetFindArgument(
        ExpressionSyntax value,
        IPropertySymbol keyProperty,
        IAnonymousFunctionOperation lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax valueExpression)
    {
        valueExpression = null!;
        if (ReferencesLambdaParameter(value, lambda, semanticModel, cancellationToken))
            return false;

        var valueType = semanticModel.GetTypeInfo(value, cancellationToken).Type;
        if (valueType == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(valueType, keyProperty.Type))
        {
            valueExpression = value.WithoutTrivia();
            return true;
        }

        var conversion = semanticModel.Compilation.ClassifyConversion(valueType, keyProperty.Type);
        if (!conversion.IsImplicit || !conversion.IsNumeric || conversion.IsNullable)
            return false;

        var keyTypeSyntax = SyntaxFactory.ParseTypeName(
            keyProperty.Type.ToMinimalDisplayString(semanticModel, value.SpanStart));
        var operand = value.WithoutTrivia();
        if (operand is not (IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or
            InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax))
        {
            operand = SyntaxFactory.ParenthesizedExpression(operand);
        }

        valueExpression = SyntaxFactory.CastExpression(keyTypeSyntax, operand);
        return true;
    }

    private static bool ReferencesLambdaParameter(
        ExpressionSyntax expression,
        IAnonymousFunctionOperation lambda,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var parameter = lambda.Symbol.Parameters.FirstOrDefault();
        if (parameter == null)
            return false;

        var operation = semanticModel.GetOperation(expression, cancellationToken)?.UnwrapConversions();
        return operation?.DescendantsAndSelf()
            .OfType<IParameterReferenceOperation>()
            .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)) == true;
    }

    private static bool TryGetPrimaryKeyProperty(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IPropertySymbol keyProperty)
    {
        keyProperty = null!;
        var operation = semanticModel.GetOperation(expression, cancellationToken)?.UnwrapConversions();
        if (operation is not IPropertyReferenceOperation propertyReference ||
            propertyReference.Instance?.UnwrapConversions() is not IParameterReferenceOperation ||
            FindInsteadOfFirstOrDefaultKeyAnalysis.TryFindSafePrimaryKey(
                propertyReference.Property.ContainingType,
                semanticModel.Compilation,
                cancellationToken) != propertyReference.Property.Name)
        {
            return false;
        }

        keyProperty = propertyReference.Property;
        return true;
    }
}
