using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;

public sealed partial class EntityMissingPrimaryKeyAnalyzer
{
    private static bool HasLocalAssemblyValueInScope(
        IdentifierNameSyntax identifier,
        CancellationToken cancellationToken)
    {
        var position = identifier.SpanStart;

        foreach (var parameterList in identifier.Ancestors().OfType<ParameterListSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parameterList.SpanStart >= position)
                continue;

            if (parameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == "Assembly"))
                return true;
        }

        foreach (var block in identifier.Ancestors().OfType<BlockSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var statement in block.Statements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (statement.SpanStart >= position)
                    break;

                if (DeclaresAssemblyValue(statement))
                    return true;
            }
        }

        return false;
    }

    private static bool DeclaresAssemblyValue(StatementSyntax statement)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration &&
            localDeclaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Assembly"))
        {
            return true;
        }

        if (statement is ForEachStatementSyntax foreachStatement &&
            foreachStatement.Identifier.ValueText == "Assembly")
        {
            return true;
        }

        if (statement.DescendantNodes().OfType<ForEachStatementSyntax>().Any(foreachStatement => foreachStatement.Identifier.ValueText == "Assembly"))
            return true;

        if (statement.DescendantNodes().OfType<CatchDeclarationSyntax>().Any(catchDeclaration => catchDeclaration.Identifier.ValueText == "Assembly"))
            return true;

        return statement.DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Any(designation => designation.Identifier.ValueText == "Assembly");
    }
}
