using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC005_MultipleOrderBy;

public sealed partial class MultipleOrderByAnalyzer
{
    private bool TryGetSingleAssignmentLocalInitializer(
        ILocalReferenceOperation localReference,
        IInvocationOperation currentInvocation,
        SemanticModel semanticModel,
        out IOperation? initializer)
    {
        initializer = null;

        var declarationSyntax = localReference.Local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();

        if (declarationSyntax?.Initializer?.Value == null)
            return false;

        if (HasWriteBeforeUse(localReference.Local, declarationSyntax, currentInvocation.Syntax, semanticModel))
            return false;

        initializer = semanticModel.GetOperation(UnwrapInitializerExpression(declarationSyntax.Initializer.Value))?.UnwrapConversions();
        return initializer != null;
    }

    private static ExpressionSyntax UnwrapInitializerExpression(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasWriteBeforeUse(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarationSyntax,
        SyntaxNode useSyntax,
        SemanticModel semanticModel)
    {
        var scope = (SyntaxNode?)declarationSyntax.FirstAncestorOrSelf<BlockSyntax>() ??
            declarationSyntax.FirstAncestorOrSelf<CompilationUnitSyntax>();
        if (scope == null)
            return false;

        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart <= declarationSyntax.SpanStart || assignment.SpanStart >= useSyntax.SpanStart)
                continue;

            if (IsWriteToLocal(assignment.Left, local, semanticModel))
                return true;
        }

        foreach (var argument in scope.DescendantNodes().OfType<ArgumentSyntax>())
        {
            if (argument.SpanStart <= declarationSyntax.SpanStart || argument.SpanStart >= useSyntax.SpanStart)
                continue;

            var keywordText = argument.RefOrOutKeyword.Text;
            if (keywordText is not ("out" or "ref"))
                continue;

            var symbol = semanticModel.GetSymbolInfo(argument.Expression).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, local))
                return true;
        }

        return false;
    }

    private static bool IsWriteToLocal(
        ExpressionSyntax target,
        ILocalSymbol local,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(target).Symbol;
        if (SymbolEqualityComparer.Default.Equals(symbol, local))
            return true;

        return target switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsWriteToLocal(parenthesized.Expression, local, semanticModel),
            TupleExpressionSyntax tuple =>
                tuple.Arguments.Any(argument => IsWriteToLocal(argument.Expression, local, semanticModel)),
            _ => false
        };
    }
}
