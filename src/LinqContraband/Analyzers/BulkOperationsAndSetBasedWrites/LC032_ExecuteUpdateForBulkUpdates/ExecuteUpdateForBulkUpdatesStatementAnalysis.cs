using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

public sealed partial class ExecuteUpdateForBulkUpdatesAnalyzer
{
    private static bool TryGetImmediatelyPreviousForEachLoop(
        IInvocationOperation invocation,
        out IForEachLoopOperation loop)
    {
        loop = null!;

        if (!TryGetImmediatelyPreviousStatement(invocation, out var previousStatement))
            return false;

        if (previousStatement is not IForEachLoopOperation forEachLoop)
            return false;

        loop = forEachLoop;
        return true;
    }

    private static bool TryGetImmediatePreviousLocalValue(
        IForEachLoopOperation loop,
        ILocalSymbol local,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (!TryGetImmediatelyPreviousStatement(loop, out var previousStatement))
            return false;

        if (previousStatement is IVariableDeclarationGroupOperation declarationGroup)
        {
            foreach (var declaration in declarationGroup.Declarations)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                        declarator.Initializer != null)
                    {
                        valueOperation = declarator.Initializer.Value;
                        return true;
                    }
                }
            }
        }

        if (previousStatement is IExpressionStatementOperation expressionStatement &&
            expressionStatement.Operation.UnwrapConversions() is ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
            SymbolEqualityComparer.Default.Equals(localReference.Local, local))
        {
            valueOperation = assignment.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetImmediatelyPreviousStatement(IOperation operation, out IOperation previousStatement)
    {
        previousStatement = null!;
        var currentStatement = FindContainingStatement(operation);

        if (currentStatement?.Parent is not IBlockOperation block)
            return false;

        for (var i = 0; i < block.Operations.Length; i++)
        {
            if (!ReferenceEquals(block.Operations[i], currentStatement))
                continue;

            if (i == 0)
                return false;

            previousStatement = block.Operations[i - 1];
            return true;
        }

        return false;
    }

    private static IOperation? FindContainingStatement(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current.Parent is IBlockOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }

    private static bool TryGetSingleAssignedLocalValue(
        ILocalSymbol local,
        IOperation analysisScope,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax() as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var semanticModel = analysisScope.SemanticModel;
        var executableRoot = analysisScope.FindOwningExecutableRoot();
        if (semanticModel == null || executableRoot == null)
            return false;

        if (HasLocalWrites(local, executableRoot.Syntax, semanticModel))
            return false;

        var operation = semanticModel.GetOperation(declarator.Initializer.Value);
        if (operation == null)
            return false;

        valueOperation = operation;
        return true;
    }

    private static bool HasLocalWrites(ILocalSymbol local, SyntaxNode executableRootSyntax, SemanticModel semanticModel)
    {
        foreach (var assignment in executableRootSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, local))
                return true;
        }

        foreach (var prefix in executableRootSyntax.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!prefix.IsKind(SyntaxKind.PreIncrementExpression) &&
                !prefix.IsKind(SyntaxKind.PreDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(prefix.Operand).Symbol, local))
                return true;
        }

        foreach (var postfix in executableRootSyntax.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!postfix.IsKind(SyntaxKind.PostIncrementExpression) &&
                !postfix.IsKind(SyntaxKind.PostDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(postfix.Operand).Symbol, local))
                return true;
        }

        return false;
    }
}
