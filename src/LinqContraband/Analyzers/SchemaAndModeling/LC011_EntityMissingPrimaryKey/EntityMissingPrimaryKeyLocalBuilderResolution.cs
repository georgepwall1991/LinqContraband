using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool TryResolveLocalBuilder(
        IdentifierNameSyntax identifier,
        Dictionary<string, INamedTypeSymbol> builderVariables,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions,
        out INamedTypeSymbol entityType)
    {
        entityType = null!;
        var identifierName = identifier.Identifier.ValueText;
        var position = identifier.SpanStart;

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (statement is not LocalDeclarationStatementSyntax localDeclaration)
                    continue;

                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (variable.Identifier.ValueText != identifierName || variable.Initializer?.Value == null)
                        continue;

                    if (TryResolveEntityTypeFromBuilderExpression(
                        variable.Initializer.Value,
                        builderVariables,
                        compilationModel,
                        cancellationToken,
                        visitedExpressions,
                        out var localEntityType))
                    {
                        entityType = localEntityType;
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
