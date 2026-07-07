using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionAnalyzer
{
    private static void CollectSyntaxBasedPropertyAccesses(
        IInvocationOperation invocation,
        ILocalSymbol variable,
        ITypeSymbol entityType,
        HashSet<ILocalSymbol> foreachLocals,
        HashSet<ILocalSymbol> manualIterationLocals,
        HashSet<string> accessedProperties,
        CancellationToken cancellationToken)
    {
        var semanticModel = invocation.SemanticModel;
        if (semanticModel == null) return;

        var scope = invocation.Syntax.FirstAncestorOrSelf<MethodDeclarationSyntax>() as SyntaxNode ??
                    invocation.Syntax.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() ??
                    invocation.Syntax.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() as SyntaxNode;
        if (scope == null) return;

        foreach (var node in scope.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                if (!IsTrackedEntitySyntax(
                        conditionalAccess.Expression,
                        variable,
                        foreachLocals,
                        manualIterationLocals,
                        semanticModel,
                        cancellationToken))
                {
                    continue;
                }

                if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                    semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol is IPropertySymbol conditionalProperty &&
                    IsPropertyOfType(conditionalProperty, entityType))
                {
                    accessedProperties.Add(conditionalProperty.Name);
                }

                continue;
            }

            if (node is not MemberAccessExpressionSyntax memberAccess)
                continue;

            if (memberAccess.Expression is not ElementAccessExpressionSyntax elementAccess) continue;
            if (!IsCollectionElementAccess(elementAccess, variable, semanticModel, cancellationToken)) continue;

            if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol elementProperty &&
                IsPropertyOfType(elementProperty, entityType))
            {
                accessedProperties.Add(elementProperty.Name);
            }
        }
    }
}
