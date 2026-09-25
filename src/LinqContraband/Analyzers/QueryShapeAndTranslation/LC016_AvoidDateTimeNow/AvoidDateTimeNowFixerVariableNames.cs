using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC016_AvoidDateTimeNow;

public sealed partial class AvoidDateTimeNowFixer
{
    private static string GetUniqueVariableName(SyntaxNode node, SemanticModel semanticModel) =>
        GetUniqueVariableName(CollectExistingNames(node, semanticModel));

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

    // A new local may not reuse any name declared anywhere in the enclosing member (CS0128/CS0136), and
    // should not shadow a field, property or other symbol the member already reads by that name.
    private static HashSet<string> CollectExistingNames(SyntaxNode node, SemanticModel semanticModel)
    {
        var existingNames = new HashSet<string>();
        var scope = node.AncestorsAndSelf().LastOrDefault(ancestor =>
            ancestor is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not NamespaceDeclarationSyntax ||
            ancestor is GlobalStatementSyntax);
        foreach (var descendant in (scope ?? node).DescendantNodes())
        {
            switch (descendant)
            {
                case VariableDeclaratorSyntax declarator:
                    existingNames.Add(declarator.Identifier.ValueText);
                    break;
                case SingleVariableDesignationSyntax designation:
                    existingNames.Add(designation.Identifier.ValueText);
                    break;
                case ForEachStatementSyntax forEach:
                    existingNames.Add(forEach.Identifier.ValueText);
                    break;
                case CatchDeclarationSyntax catchDeclaration:
                    existingNames.Add(catchDeclaration.Identifier.ValueText);
                    break;
                case ParameterSyntax parameter:
                    existingNames.Add(parameter.Identifier.ValueText);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    existingNames.Add(localFunction.Identifier.ValueText);
                    break;
                case FromClauseSyntax fromClause:
                    existingNames.Add(fromClause.Identifier.ValueText);
                    break;
                case LetClauseSyntax letClause:
                    existingNames.Add(letClause.Identifier.ValueText);
                    break;
                case JoinClauseSyntax joinClause:
                    existingNames.Add(joinClause.Identifier.ValueText);
                    break;
                case JoinIntoClauseSyntax joinInto:
                    existingNames.Add(joinInto.Identifier.ValueText);
                    break;
                case QueryContinuationSyntax continuation:
                    existingNames.Add(continuation.Identifier.ValueText);
                    break;
            }
        }

        foreach (var symbol in semanticModel.LookupSymbols(node.SpanStart))
            existingNames.Add(symbol.Name);

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
