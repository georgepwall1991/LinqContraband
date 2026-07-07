using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool TryGetLocalAssignment(
        StatementSyntax statement,
        string localName,
        out ExpressionSyntax assignedValue)
    {
        assignedValue = null!;
        if (statement is not ExpressionStatementSyntax expressionStatement ||
            expressionStatement.Expression is not AssignmentExpressionSyntax assignment ||
            assignment.Left is not IdentifierNameSyntax identifier ||
            identifier.Identifier.ValueText != localName)
        {
            return false;
        }

        assignedValue = assignment.Right;
        return true;
    }

    private static bool ContainsLocalAssignment(SyntaxNode node, string localName)
    {
        if (node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            return false;

        return node.DescendantNodes(descendIntoChildren: child =>
                child is not AnonymousFunctionExpressionSyntax &&
                child is not LocalFunctionStatementSyntax)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is IdentifierNameSyntax identifier &&
                               identifier.Identifier.ValueText == localName);
    }
}
