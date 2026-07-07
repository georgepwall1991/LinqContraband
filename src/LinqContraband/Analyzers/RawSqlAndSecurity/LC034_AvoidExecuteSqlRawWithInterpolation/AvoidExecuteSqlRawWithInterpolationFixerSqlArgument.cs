using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC034_AvoidExecuteSqlRawWithInterpolation;

public sealed partial class AvoidExecuteSqlRawWithInterpolationFixer
{
    private static ArgumentSyntax? GetSqlArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText == "sql")
                return argument;
        }

        return invocation.ArgumentList.Arguments.FirstOrDefault(argument => argument.NameColon is null);
    }

    private static bool HasInterpolationInsideSqlStringLiteral(InterpolatedStringExpressionSyntax interpolatedSql)
    {
        var insideSqlStringLiteral = false;
        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    insideSqlStringLiteral = ToggleSqlStringLiteralState(text.TextToken.ValueText, insideSqlStringLiteral);
                    break;
                case InterpolationSyntax when insideSqlStringLiteral:
                    return true;
            }
        }

        return false;
    }

    private static bool ToggleSqlStringLiteralState(string text, bool insideSqlStringLiteral)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\'')
                continue;

            if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                index++;
                continue;
            }

            insideSqlStringLiteral = !insideSqlStringLiteral;
        }

        return insideSqlStringLiteral;
    }
}
