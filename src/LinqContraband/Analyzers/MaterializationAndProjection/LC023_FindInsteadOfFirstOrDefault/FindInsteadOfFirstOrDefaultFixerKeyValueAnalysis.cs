using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
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
        if (IsPrimaryKeyAccess(binary.Left, semanticModel, cancellationToken))
        {
            if (ReferencesLambdaParameter(binary.Right, lambda, semanticModel, cancellationToken))
            {
                valueExpression = null!;
                return false;
            }

            valueExpression = binary.Right.WithoutTrivia();
            return true;
        }

        if (IsPrimaryKeyAccess(binary.Right, semanticModel, cancellationToken))
        {
            if (ReferencesLambdaParameter(binary.Left, lambda, semanticModel, cancellationToken))
            {
                valueExpression = null!;
                return false;
            }

            valueExpression = binary.Left.WithoutTrivia();
            return true;
        }

        valueExpression = null!;
        return false;
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
}
