using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionFixer
{
    private static bool HasUnsupportedEntityPropertyAccess(
        SyntaxNode containingMethod,
        ILocalSymbol collectionVariable,
        HashSet<ILocalSymbol> trackedEntityLocals,
        ITypeSymbol entityType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var memberAccess in containingMethod.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (semanticModel.GetOperation(memberAccess, cancellationToken) is not IPropertyReferenceOperation propertyReference ||
                !IsPropertyOfType(propertyReference.Property, entityType))
            {
                continue;
            }

            if (IsTrackedEntityExpression(memberAccess.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken))
            {
                continue;
            }

            if (propertyReference.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
                trackedEntityLocals.Contains(localReference.Local))
            {
                return true;
            }
        }

        foreach (var conditionalAccess in containingMethod.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (conditionalAccess.WhenNotNull is not MemberBindingExpressionSyntax memberBinding ||
                semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol is not IPropertySymbol conditionalProperty ||
                !IsPropertyOfType(conditionalProperty, entityType))
            {
                continue;
            }

            if (IsTrackedEntityExpression(conditionalAccess.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken))
            {
                continue;
            }

            if (IsTrackedEntityConversionExpression(conditionalAccess.Expression, collectionVariable, trackedEntityLocals, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnsafeIndexedEntityAccess(
        SyntaxNode containingMethod,
        ILocalSymbol collectionVariable,
        ITypeSymbol entityType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var elementAccess in containingMethod.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (semanticModel.GetSymbolInfo(elementAccess.Expression, cancellationToken).Symbol is not ILocalSymbol collectionSymbol ||
                !SymbolEqualityComparer.Default.Equals(collectionSymbol, collectionVariable))
            {
                continue;
            }

            if (elementAccess.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Expression, elementAccess) &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol directProperty &&
                IsPropertyOfType(directProperty, entityType))
            {
                continue;
            }

            if (elementAccess.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                ReferenceEquals(conditionalAccess.Expression, elementAccess) &&
                conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol is IPropertySymbol conditionalProperty &&
                IsPropertyOfType(conditionalProperty, entityType))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsPropertyOfType(IPropertySymbol property, ITypeSymbol entityType)
    {
        var propContainingType = property.ContainingType;
        if (propContainingType == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(propContainingType, entityType))
            return true;

        if (entityType.AllInterfaces.Contains(propContainingType, SymbolEqualityComparer.Default))
            return true;

        var current = entityType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(propContainingType, current))
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
