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
    private static InvocationExpressionSyntax? FindAsNoTrackingOrigin(
        SyntaxNode root,
        SemanticModel semanticModel,
        ILocalSymbol local,
        int boundary,
        CancellationToken cancellationToken)
    {
        var origins = new List<AsNoTrackingOrigin>();

        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer == null || declarator.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(declarator, cancellationToken), local)) continue;

            AddOrigin(declarator.Initializer.Value, declarator.SpanStart);
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

            AddOrigin(assignment.Right, assignment.SpanStart);
        }

        foreach (var forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (!forEach.Span.Contains(boundary) || forEach.Expression.SpanStart >= boundary) continue;
            if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(forEach, cancellationToken), local)) continue;

            AddOrigin(forEach.Expression, forEach.Expression.SpanStart);
        }

        if (origins.Count == 0) return null;

        var best = origins[0];
        for (var i = 1; i < origins.Count; i++)
        {
            if (origins[i].Position >= best.Position)
                best = origins[i];
        }

        if (IsConditionalRelativeTo(best.Syntax, boundary, root) &&
            origins.Any(origin => origin.Position < best.Position))
        {
            return null;
        }

        return best.Invocation;

        void AddOrigin(ExpressionSyntax expression, int position)
        {
            if (!IsNoTrackingSource(root, semanticModel, expression, position, cancellationToken, new HashSet<ISymbol>(SymbolEqualityComparer.Default)))
                return;

            var invocation = FindAsNoTrackingInvocation(expression);
            origins.Add(new AsNoTrackingOrigin(position, invocation, expression));
        }
    }

    private readonly struct AsNoTrackingOrigin
    {
        public AsNoTrackingOrigin(int position, InvocationExpressionSyntax? invocation, SyntaxNode syntax)
        {
            Position = position;
            Invocation = invocation;
            Syntax = syntax;
        }

        public int Position { get; }
        public InvocationExpressionSyntax? Invocation { get; }
        public SyntaxNode Syntax { get; }
    }

    private static bool IsConditionalRelativeTo(SyntaxNode originSyntax, int usePosition, SyntaxNode rootSyntax)
    {
        for (var node = originSyntax.Parent; node != null && node != rootSyntax; node = node.Parent)
        {
            var isBranching = node is IfStatementSyntax
                or SwitchStatementSyntax
                or SwitchExpressionSyntax
                or ConditionalExpressionSyntax
                or CatchClauseSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or CommonForEachStatementSyntax;

            if (isBranching && !node.Span.Contains(usePosition))
                return true;
        }

        return false;
    }
}
