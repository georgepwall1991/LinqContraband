using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
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
        var insideBracketIdentifier = false;
        var insideDoubleQuotedIdentifier = false;
        var insideBacktickIdentifier = false;

        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    UpdateSqlDelimiterState(
                        text.TextToken.ValueText,
                        ref insideSqlStringLiteral,
                        ref insideBracketIdentifier,
                        ref insideDoubleQuotedIdentifier,
                        ref insideBacktickIdentifier);
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
        var insideBracketIdentifier = false;
        var insideDoubleQuotedIdentifier = false;
        var insideBacktickIdentifier = false;
        var sqlBeforeInterpolation = string.Empty;

        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    var textValue = text.TextToken.ValueText;
                    UpdateSqlDelimiterState(
                        textValue,
                        ref insideSqlStringLiteral,
                        ref insideBracketIdentifier,
                        ref insideDoubleQuotedIdentifier,
                        ref insideBacktickIdentifier);
                    sqlBeforeInterpolation += textValue;
                    break;
                case InterpolationSyntax:
                    if (!insideSqlStringLiteral &&
                        !insideBracketIdentifier &&
                        !insideDoubleQuotedIdentifier &&
                        !insideBacktickIdentifier &&
                        !IsLikelySqlValuePosition(sqlBeforeInterpolation))
                        return true;

                    sqlBeforeInterpolation += "?";
                    break;
            }
        }

        return false;
    }

    private static bool HasInterpolationInsideDelimitedIdentifier(
        InterpolatedStringExpressionSyntax interpolatedSql)
    {
        var insideSqlStringLiteral = false;
        var insideBracketIdentifier = false;
        var insideDoubleQuotedIdentifier = false;
        var insideBacktickIdentifier = false;

        foreach (var content in interpolatedSql.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    UpdateSqlDelimiterState(
                        text.TextToken.ValueText,
                        ref insideSqlStringLiteral,
                        ref insideBracketIdentifier,
                        ref insideDoubleQuotedIdentifier,
                        ref insideBacktickIdentifier);
                    break;
                case InterpolationSyntax when
                    insideBracketIdentifier || insideDoubleQuotedIdentifier || insideBacktickIdentifier:
                    return true;
            }
        }

        return false;
    }

    private static bool HasInterpolationWithoutProvenParameterType(
        InterpolatedStringExpressionSyntax interpolatedSql,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var interpolation in interpolatedSql.Contents.OfType<InterpolationSyntax>())
        {
            var type = semanticModel.GetTypeInfo(interpolation.Expression, cancellationToken).Type;
            if (!IsProvenSqlParameterType(type, semanticModel.Compilation))
                return true;
        }

        return false;
    }

    private static bool IsLikelySqlValuePosition(string sqlBeforeInterpolation)
    {
        var trimmed = sqlBeforeInterpolation.TrimEnd();
        if (trimmed.Length == 0 ||
            trimmed.EndsWith(".", System.StringComparison.Ordinal) ||
            !StartsWithSupportedDmlCommand(trimmed) ||
            ContainsAmbiguousSqlBoundary(trimmed))
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

        return false;
    }

    private static bool IsProvenSqlParameterType(ITypeSymbol? type, Compilation compilation)
    {
        if (type is INamedTypeSymbol nullable &&
            nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return IsProvenSqlParameterType(nullable.TypeArguments[0], compilation);

        if (type is null || type.TypeKind is TypeKind.Enum or TypeKind.TypeParameter)
            return false;

        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Char or
            SpecialType.System_DateTime)
            return true;

        return type is INamedTypeSymbol namedType &&
               SymbolEqualityComparer.Default.Equals(
                   namedType.ContainingAssembly,
                   compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly) &&
               namedType.ContainingNamespace.ToDisplayString() == "System" &&
               namedType.Name is "DateTimeOffset" or "Guid" or "TimeSpan";
    }

    private static bool StartsWithSupportedDmlCommand(string sql)
    {
        var trimmed = sql.TrimStart();
        var wordLength = 0;
        while (wordLength < trimmed.Length && char.IsLetter(trimmed[wordLength]))
            wordLength++;

        if (wordLength == 0)
            return false;

        var command = trimmed.Substring(0, wordLength);
        return command.Equals("UPDATE", System.StringComparison.OrdinalIgnoreCase) ||
               command.Equals("DELETE", System.StringComparison.OrdinalIgnoreCase) ||
               command.Equals("INSERT", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAmbiguousSqlBoundary(string sql)
    {
        return sql.IndexOf(';') >= 0 ||
               sql.IndexOf("--", System.StringComparison.Ordinal) >= 0 ||
               sql.IndexOf("/*", System.StringComparison.Ordinal) >= 0;
    }

    private static bool ContainsAmbiguousSqlBoundary(InterpolatedStringExpressionSyntax interpolatedSql)
    {
        return interpolatedSql.Contents
            .OfType<InterpolatedStringTextSyntax>()
            .Any(text => ContainsAmbiguousSqlBoundary(text.TextToken.ValueText));
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

    private static void UpdateSqlDelimiterState(
        string text,
        ref bool insideSqlStringLiteral,
        ref bool insideBracketIdentifier,
        ref bool insideDoubleQuotedIdentifier,
        ref bool insideBacktickIdentifier)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (insideSqlStringLiteral)
            {
                if (character != '\'' || IsBackslashEscaped(text, index))
                    continue;

                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                insideSqlStringLiteral = false;
                continue;
            }

            if (insideBracketIdentifier)
            {
                if (character != ']')
                    continue;

                if (index + 1 < text.Length && text[index + 1] == ']')
                {
                    index++;
                    continue;
                }

                insideBracketIdentifier = false;
                continue;
            }

            if (insideDoubleQuotedIdentifier)
            {
                if (character != '"')
                    continue;

                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                insideDoubleQuotedIdentifier = false;
                continue;
            }

            if (insideBacktickIdentifier)
            {
                if (character != '`')
                    continue;

                if (index + 1 < text.Length && text[index + 1] == '`')
                {
                    index++;
                    continue;
                }

                insideBacktickIdentifier = false;
                continue;
            }

            switch (character)
            {
                case '\'':
                    insideSqlStringLiteral = true;
                    break;
                case '[':
                    insideBracketIdentifier = true;
                    break;
                case '"':
                    insideDoubleQuotedIdentifier = true;
                    break;
                case '`':
                    insideBacktickIdentifier = true;
                    break;
            }
        }
    }

    private static bool IsBackslashEscaped(string text, int quoteIndex)
    {
        var backslashCount = 0;
        for (var index = quoteIndex - 1; index >= 0 && text[index] == '\\'; index--)
            backslashCount++;

        return backslashCount % 2 == 1;
    }
}
