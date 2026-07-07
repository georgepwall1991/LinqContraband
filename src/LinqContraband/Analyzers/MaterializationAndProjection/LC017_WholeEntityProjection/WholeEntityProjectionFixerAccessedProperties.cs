using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC017_WholeEntityProjection;

public sealed partial class WholeEntityProjectionFixer
{
    private static HashSet<string> FindAccessedProperties(
        SyntaxNode root,
        ILocalSymbol variableSymbol,
        ITypeSymbol entityType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var properties = new HashSet<string>();
        var containingMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
            {
                var span = m.Span;
                return root.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Any(v => v.Identifier.Text == variableSymbol.Name && span.Contains(v.Span));
            });

        if (containingMethod == null)
            return properties;

        if (HasUnsafeIndexedEntityAccess(containingMethod, variableSymbol, entityType, semanticModel, cancellationToken))
            return properties;

        var trackedEntityLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var forEach in containingMethod.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (semanticModel.GetSymbolInfo(forEach.Expression, cancellationToken).Symbol is not ILocalSymbol collectionSymbol ||
                !SymbolEqualityComparer.Default.Equals(collectionSymbol, variableSymbol))
                continue;

            if (semanticModel.GetDeclaredSymbol(forEach, cancellationToken) is ILocalSymbol iterationSymbol)
                trackedEntityLocals.Add(iterationSymbol);
        }

        if (HasUnsupportedEntityPropertyAccess(containingMethod, variableSymbol, trackedEntityLocals, entityType, semanticModel, cancellationToken))
            return properties;

        foreach (var node in containingMethod.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node)
            {
                case MemberAccessExpressionSyntax memberAccess
                    when IsTrackedEntityExpression(memberAccess.Expression, variableSymbol, trackedEntityLocals, semanticModel, cancellationToken) &&
                         semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol directProperty &&
                         IsPropertyOfType(directProperty, entityType):
                    properties.Add(directProperty.Name);
                    break;

                case ConditionalAccessExpressionSyntax conditionalAccess
                    when IsTrackedEntityExpression(conditionalAccess.Expression, variableSymbol, trackedEntityLocals, semanticModel, cancellationToken) &&
                         conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                         semanticModel.GetSymbolInfo(memberBinding, cancellationToken).Symbol is IPropertySymbol conditionalProperty &&
                         IsPropertyOfType(conditionalProperty, entityType):
                    properties.Add(conditionalProperty.Name);
                    break;
            }
        }

        return properties;
    }
}
