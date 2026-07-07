using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC025_AsNoTrackingWithUpdate;

public sealed partial class AsNoTrackingWithUpdateFixer
{
    private static bool IsNoTrackingSource(
        SyntaxNode root,
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        int boundary,
        CancellationToken cancellationToken,
        ISet<ISymbol> visited)
    {
        var operation = semanticModel.GetOperation(expression, cancellationToken)?.UnwrapConversions();
        if (operation == null)
            return false;

        if (operation is IInvocationOperation invocation)
        {
            if (HasAsNoTrackingInChain(invocation))
                return true;

            if (invocation.TargetMethod.Name.IsMaterializerMethod() &&
                invocation.GetInvocationReceiver() is ILocalReferenceOperation receiverLocal)
            {
                return IsLocalFromNoTracking(root, semanticModel, receiverLocal.Local, boundary, cancellationToken, visited);
            }
        }

        return operation is ILocalReferenceOperation localReference &&
               IsLocalFromNoTracking(root, semanticModel, localReference.Local, boundary, cancellationToken, visited);
    }

    private static bool IsLocalFromNoTracking(
        SyntaxNode root,
        SemanticModel semanticModel,
        ILocalSymbol local,
        int boundary,
        CancellationToken cancellationToken,
        ISet<ISymbol> visited)
    {
        if (!visited.Add(local)) return false;

        AsNoTrackingOrigin? bestOrigin = null;

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer == null || declarator.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), local)) continue;

            if (bestOrigin == null || declarator.SpanStart >= bestOrigin.Value.Position)
                bestOrigin = new AsNoTrackingOrigin(declarator.SpanStart, null, declarator.Initializer.Value);
        }

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.SpanStart >= boundary) continue;

            var target = semanticModel.GetOperation(assignment.Left, cancellationToken)?.UnwrapConversions();
            if (target is not ILocalReferenceOperation localReference ||
                !SymbolEqualityComparer.Default.Equals(localReference.Local, local))
            {
                continue;
            }

            if (bestOrigin == null || assignment.SpanStart >= bestOrigin.Value.Position)
                bestOrigin = new AsNoTrackingOrigin(assignment.SpanStart, null, assignment.Right);
        }

        return bestOrigin != null &&
               bestOrigin.Value.Syntax is ExpressionSyntax expression &&
               IsNoTrackingSource(root, semanticModel, expression, bestOrigin.Value.Position, cancellationToken, visited);
    }
}
