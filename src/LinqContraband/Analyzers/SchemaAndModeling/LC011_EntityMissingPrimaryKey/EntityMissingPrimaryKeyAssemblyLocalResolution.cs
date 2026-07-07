using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool HasAssemblyMember(INamedTypeSymbol dbContextType)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.BaseType)
        {
            if (currentType.GetMembers("Assembly").Any(member => member is IFieldSymbol or IPropertySymbol or IMethodSymbol))
                return true;
        }

        return false;
    }

    private static bool TryResolveLocalCurrentAssembly(
        IdentifierNameSyntax identifier,
        INamedTypeSymbol dbContextType,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions,
        out bool isCurrentAssembly)
    {
        isCurrentAssembly = false;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpressionSyntax? latestValue = null;
            var localFound = false;

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                {
                    if (TryGetLocalAssignment(statement, identifierName, out var assignedValue))
                    {
                        localFound = true;
                        latestValue = assignedValue;
                    }
                    else if (ContainsLocalAssignment(statement, identifierName))
                    {
                        return true;
                    }

                    continue;
                }

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText == identifierName)
                    {
                        localFound = true;
                        latestValue = variable.Initializer?.Value;
                    }
                }
            }

            if (latestValue != null)
            {
                isCurrentAssembly = IsCurrentAssemblyExpression(latestValue, dbContextType, compilationModel, cancellationToken, visitedExpressions);
                return true;
            }

            if (localFound)
                return true;
        }

        return false;
    }

}
