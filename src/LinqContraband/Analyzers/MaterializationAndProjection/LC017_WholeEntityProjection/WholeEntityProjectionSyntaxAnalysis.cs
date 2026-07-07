using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
    private static bool IsPropertyOfType(IPropertySymbol property, ITypeSymbol entityType)
    {
        if (SymbolEqualityComparer.Default.Equals(property.ContainingType, entityType)) return true;
        if (entityType.AllInterfaces.Contains(property.ContainingType, SymbolEqualityComparer.Default)) return true;
        return InheritsFrom(entityType, property.ContainingType);
    }

    private static bool IsTrackedEntitySyntax(
        ExpressionSyntax expression,
        ILocalSymbol variable,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is ElementAccessExpressionSyntax elementAccess)
            return IsCollectionElementAccess(elementAccess, variable, semanticModel, cancellationToken);

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol as ILocalSymbol;
        if (symbol == null) return false;

        return SymbolEqualityComparer.Default.Equals(symbol, variable) ||
               foreachLocals.Contains(symbol) ||
               manualIterationLocals.Contains(symbol);
    }

    private static bool IsCollectionElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        ILocalSymbol variable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(elementAccess.Expression, cancellationToken).Symbol as ILocalSymbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, variable);
    }

    private static bool IsIndexedAccessOf(IOperation operation, ILocalSymbol collectionVar)
    {
        var unwrapped = operation.UnwrapConversions();
        if (unwrapped is IPropertyReferenceOperation propRef && propRef.Arguments.Length > 0)
        {
            var instance = propRef.Instance?.UnwrapConversions();
            if (instance is ILocalReferenceOperation localRef &&
                SymbolEqualityComparer.Default.Equals(localRef.Local, collectionVar))
                return true;
        }
        return false;
    }

    private static bool InheritsFrom(ITypeSymbol type, ITypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static IOperation? FindMethodBody(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current is IMethodBodyOperation ||
                current is IBlockOperation { Parent: IMethodBodyOperation } ||
                current is ILocalFunctionOperation)
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }
}
