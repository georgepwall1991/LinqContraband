using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool TryResolveMemberCurrentAssembly(
        INamedTypeSymbol dbContextType,
        string memberName,
        CompilationModel compilationModel,
        CancellationToken cancellationToken,
        HashSet<ExpressionSyntax> visitedExpressions)
    {
        for (var currentType = dbContextType; currentType != null; currentType = currentType.BaseType)
        {
            var members = currentType.GetMembers(memberName);
            if (members.IsEmpty)
                continue;

            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member.DeclaringSyntaxReferences.IsEmpty)
                    continue;

                foreach (var syntaxRef in member.DeclaringSyntaxReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var syntax = syntaxRef.GetSyntax(cancellationToken);
                    ExpressionSyntax? initializer = syntax switch
                    {
                        VariableDeclaratorSyntax variable when member is IFieldSymbol { IsReadOnly: true } => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property when member is IPropertySymbol { SetMethod: null } => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                        _ => null
                    };

                    if (initializer != null &&
                        IsCurrentAssemblyExpression(initializer, dbContextType, compilationModel, cancellationToken, visitedExpressions))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return false;
    }
}
