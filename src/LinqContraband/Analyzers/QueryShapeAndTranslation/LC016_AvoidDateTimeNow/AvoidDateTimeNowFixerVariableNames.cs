using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static string GetUniqueVariableName(SyntaxNode node) =>
        GetUniqueVariableName(CollectExistingNames(node));

    private static string GetUniqueVariableName(HashSet<string> existingNames)
    {
        const string baseName = "now";
        if (!existingNames.Contains(baseName)) return baseName;

        for (var i = 1; i < 100; i++)
        {
            var candidate = baseName + i;
            if (!existingNames.Contains(candidate)) return candidate;
        }

        return baseName;
    }

    private static HashSet<string> CollectExistingNames(SyntaxNode node)
    {
        var existingNames = new HashSet<string>();
        var block = node.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        if (block != null)
        {
            foreach (var descendant in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                existingNames.Add(descendant.Identifier.Text);
            }
        }

        AddEnclosingParameterNames(node, existingNames);
        return existingNames;
    }

    private static void AddEnclosingParameterNames(SyntaxNode node, HashSet<string> existingNames)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case BaseMethodDeclarationSyntax methodDeclaration:
                    AddParameterNames(methodDeclaration.ParameterList, existingNames);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    AddParameterNames(localFunction.ParameterList, existingNames);
                    break;
                case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                    AddParameterNames(parenthesizedLambda.ParameterList, existingNames);
                    break;
                case SimpleLambdaExpressionSyntax simpleLambda:
                    existingNames.Add(simpleLambda.Parameter.Identifier.Text);
                    break;
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    AddParameterNames(anonymousMethod.ParameterList, existingNames);
                    break;
            }
        }
    }

    private static void AddParameterNames(ParameterListSyntax? parameterList, HashSet<string> existingNames)
    {
        if (parameterList == null) return;

        foreach (var parameter in parameterList.Parameters)
        {
            existingNames.Add(parameter.Identifier.Text);
        }
    }
}
