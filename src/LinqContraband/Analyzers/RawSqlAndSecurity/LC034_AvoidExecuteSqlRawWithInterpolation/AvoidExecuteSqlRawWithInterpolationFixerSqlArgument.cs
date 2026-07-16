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

        return StartsWithSqlCommand(sqlBeforeInterpolation, "INSERT") &&
               !HasOnlyDirectInsertValues(sqlBeforeInterpolation);
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
            if (interpolation.AlignmentClause is not null || interpolation.FormatClause is not null)
                return true;

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

        if (IsLikelyInsertValuesPosition(trimmed))
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

    private static bool IsLikelyInsertValuesPosition(string sqlBeforeInterpolation)
    {
        if (!StartsWithSqlCommand(sqlBeforeInterpolation, "INSERT"))
            return false;

        var valuesIndex = LastIndexOfSqlWord(sqlBeforeInterpolation, "VALUES");
        if (valuesIndex < 0)
            return false;

        var tail = sqlBeforeInterpolation.Substring(valuesIndex + "VALUES".Length);
        var trimmedTail = tail.TrimEnd();
        if (trimmedTail.Length == 0 ||
            (trimmedTail[trimmedTail.Length - 1] != '(' && trimmedTail[trimmedTail.Length - 1] != ','))
            return false;

        return tail.All(character =>
            char.IsWhiteSpace(character) || character is '(' or ')' or ',' or '?');
    }

    private static bool HasOnlyDirectInsertValues(string sql)
    {
        var valuesIndex = LastIndexOfSqlWord(sql, "VALUES");
        if (valuesIndex < 0)
            return false;

        var tail = sql.Substring(valuesIndex + "VALUES".Length);
        var index = 0;
        SkipWhitespace(tail, ref index);

        var hasRow = false;
        while (index < tail.Length)
        {
            if (tail[index] != '(')
                return false;

            index++;
            while (true)
            {
                SkipWhitespace(tail, ref index);
                if (index >= tail.Length || tail[index] != '?')
                    return false;

                index++;
                SkipWhitespace(tail, ref index);
                if (index >= tail.Length)
                    return false;

                if (tail[index] == ')')
                {
                    index++;
                    break;
                }

                if (tail[index] != ',')
                    return false;

                index++;
            }

            hasRow = true;
            SkipWhitespace(tail, ref index);
            if (index == tail.Length)
                return hasRow;

            if (tail[index] != ',')
                return false;

            index++;
            SkipWhitespace(tail, ref index);
        }

        return false;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
    }

    private static int LastIndexOfSqlWord(string sql, string word)
    {
        for (var index = sql.Length - word.Length; index >= 0; index--)
        {
            if (string.Compare(sql, index, word, 0, word.Length, System.StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            var hasWordBefore = index > 0 && IsSqlWordCharacter(sql[index - 1]);
            var afterIndex = index + word.Length;
            var hasWordAfter = afterIndex < sql.Length && IsSqlWordCharacter(sql[afterIndex]);
            if (!hasWordBefore && !hasWordAfter)
                return index;
        }

        return -1;
    }

    private static bool IsSqlWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
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
        return StartsWithSqlCommand(sql, "UPDATE") ||
               StartsWithSqlCommand(sql, "DELETE") ||
               StartsWithSqlCommand(sql, "INSERT");
    }

    private static bool StartsWithSqlCommand(string sql, string command)
    {
        var trimmed = sql.TrimStart();
        var wordLength = 0;
        while (wordLength < trimmed.Length && char.IsLetter(trimmed[wordLength]))
            wordLength++;

        if (wordLength == 0)
            return false;

        return trimmed.Substring(0, wordLength).Equals(command, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAmbiguousSqlBoundary(string sql)
    {
        return sql.IndexOf(';') >= 0 ||
               sql.IndexOf("--", System.StringComparison.Ordinal) >= 0 ||
               sql.IndexOf("/*", System.StringComparison.Ordinal) >= 0 ||
               sql.IndexOf('#') >= 0 ||
               LastIndexOfSqlWord(sql, "GO") >= 0 ||
               ContainsAdditionalSqlStatement(sql) ||
               ContainsPostgreSqlDollarQuoteDelimiter(sql);
    }

    private static bool ContainsAmbiguousSqlBoundary(InterpolatedStringExpressionSyntax interpolatedSql)
    {
        var sql = string.Empty;
        foreach (var content in interpolatedSql.Contents)
        {
            sql += content is InterpolatedStringTextSyntax text
                ? text.TextToken.ValueText
                : "?";
        }

        return ContainsAmbiguousSqlBoundary(sql);
    }

    private static bool ContainsAdditionalSqlStatement(string sql)
    {
        if (CountSqlWord(sql, "UPDATE") +
            CountSqlWord(sql, "DELETE") +
            CountSqlWord(sql, "INSERT") > 1)
            return true;

        return LastIndexOfSqlWord(sql, "ALTER") >= 0 ||
               LastIndexOfSqlWord(sql, "ANALYZE") >= 0 ||
               LastIndexOfSqlWord(sql, "ATTACH") >= 0 ||
               LastIndexOfSqlWord(sql, "BEGIN") >= 0 ||
               LastIndexOfSqlWord(sql, "CALL") >= 0 ||
               LastIndexOfSqlWord(sql, "COMMIT") >= 0 ||
               LastIndexOfSqlWord(sql, "COPY") >= 0 ||
               LastIndexOfSqlWord(sql, "CREATE") >= 0 ||
               LastIndexOfSqlWord(sql, "DECLARE") >= 0 ||
               LastIndexOfSqlWord(sql, "DENY") >= 0 ||
               LastIndexOfSqlWord(sql, "DETACH") >= 0 ||
               LastIndexOfSqlWord(sql, "DO") >= 0 ||
               LastIndexOfSqlWord(sql, "DROP") >= 0 ||
               LastIndexOfSqlWord(sql, "EXEC") >= 0 ||
               LastIndexOfSqlWord(sql, "EXECUTE") >= 0 ||
               LastIndexOfSqlWord(sql, "GRANT") >= 0 ||
               LastIndexOfSqlWord(sql, "MERGE") >= 0 ||
               LastIndexOfSqlWord(sql, "PRINT") >= 0 ||
               LastIndexOfSqlWord(sql, "RAISERROR") >= 0 ||
               LastIndexOfSqlWord(sql, "REINDEX") >= 0 ||
               LastIndexOfSqlWord(sql, "REPLACE") >= 0 ||
               LastIndexOfSqlWord(sql, "REVOKE") >= 0 ||
               LastIndexOfSqlWord(sql, "ROLLBACK") >= 0 ||
               LastIndexOfSqlWord(sql, "SELECT") >= 0 ||
               LastIndexOfSqlWord(sql, "THROW") >= 0 ||
               LastIndexOfSqlWord(sql, "TRUNCATE") >= 0 ||
               LastIndexOfSqlWord(sql, "USE") >= 0 ||
               LastIndexOfSqlWord(sql, "VACUUM") >= 0;
    }

    private static int CountSqlWord(string sql, string word)
    {
        var count = 0;
        for (var index = 0; index <= sql.Length - word.Length; index++)
        {
            if (string.Compare(sql, index, word, 0, word.Length, System.StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            var hasWordBefore = index > 0 && IsSqlWordCharacter(sql[index - 1]);
            var afterIndex = index + word.Length;
            var hasWordAfter = afterIndex < sql.Length && IsSqlWordCharacter(sql[afterIndex]);
            if (!hasWordBefore && !hasWordAfter)
            {
                count++;
                index += word.Length - 1;
            }
        }

        return count;
    }

    private static bool ContainsPostgreSqlDollarQuoteDelimiter(string sql)
    {
        for (var index = 0; index < sql.Length; index++)
        {
            if (sql[index] != '$')
                continue;

            var tagIndex = index + 1;
            if (tagIndex < sql.Length && sql[tagIndex] == '$')
                return true;

            if (tagIndex >= sql.Length || (!char.IsLetter(sql[tagIndex]) && sql[tagIndex] != '_'))
                continue;

            tagIndex++;
            while (tagIndex < sql.Length && IsSqlWordCharacter(sql[tagIndex]))
                tagIndex++;

            if (tagIndex < sql.Length && sql[tagIndex] == '$')
                return true;
        }

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
