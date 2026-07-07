using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

public sealed partial class AvoidFromSqlRawWithInterpolationFixer
{
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

    private static bool HasInterpolationOutsideLikelySqlValuePosition(InterpolatedStringExpressionSyntax interpolatedSql)
    {
        var insideSqlStringLiteral = false;
        var sqlBeforeInterpolation = string.Empty;

        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    var textValue = text.TextToken.ValueText;
                    insideSqlStringLiteral = ToggleSqlStringLiteralState(textValue, insideSqlStringLiteral);
                    sqlBeforeInterpolation += textValue;
                    break;
                case InterpolationSyntax:
                    if (!insideSqlStringLiteral && !IsLikelySqlValuePosition(sqlBeforeInterpolation))
                        return true;

                    sqlBeforeInterpolation += " ";
                    break;
            }
        }

        return false;
    }

    private static bool IsLikelySqlValuePosition(string sqlBeforeInterpolation)
    {
        var trimmed = sqlBeforeInterpolation.TrimEnd();
        if (trimmed.Length == 0 || trimmed.EndsWith(".", System.StringComparison.Ordinal))
            return false;

        if (EndsWithSqlValueOperator(trimmed))
            return true;

        var words = trimmed.Split(
            new[] { ' ', '\t', '\r', '\n', '(', ')', ',', ';', '=' },
            System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        var lastWord = NormalizeSqlWord(words[words.Length - 1]);
        if (lastWord is "LIKE" or "ILIKE")
            return true;

        if (trimmed.EndsWith("(", System.StringComparison.Ordinal) && lastWord == "IN")
            return true;

        return false;
    }

    private static bool EndsWithSqlValueOperator(string text)
    {
        return text.EndsWith("=", System.StringComparison.Ordinal) ||
               text.EndsWith("<", System.StringComparison.Ordinal) ||
               text.EndsWith(">", System.StringComparison.Ordinal) ||
               text.EndsWith("<=", System.StringComparison.Ordinal) ||
               text.EndsWith(">=", System.StringComparison.Ordinal) ||
               text.EndsWith("<>", System.StringComparison.Ordinal) ||
               text.EndsWith("!=", System.StringComparison.Ordinal);
    }

    private static string NormalizeSqlWord(string word)
    {
        return word.Trim('[', ']', '"', '`').ToUpperInvariant();
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
