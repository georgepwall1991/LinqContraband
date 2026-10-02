using System;
using System.Collections.Generic;
using System.Text;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization;

public sealed partial class UnboundedQueryMaterializationAnalyzer
{
    private const string ParameterToken = "?";
    private const string NumberToken = "#";
    private const string OpenParenToken = "(";
    private const string CountGroupToken = "(#)";

    /// <summary>
    /// <c>FromSql</c>/<c>FromSqlRaw</c>/<c>FromSqlInterpolated</c> over constant SQL that limits rows at the outer level
    /// (<c>LIMIT n</c>, <c>TOP n</c>, <c>FETCH FIRST/NEXT n ROWS</c>). A limit inside parentheses (a subquery or CTE body),
    /// a comment or a quoted literal does not count, and neither does a top-level set operator, where a limit may cover
    /// only one branch.
    /// </summary>
    private static bool IsRowLimitedRawSqlRoot(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name is not ("FromSql" or "FromSqlRaw" or "FromSqlInterpolated") ||
            method.ContainingNamespace?.ToDisplayString() != "Microsoft.EntityFrameworkCore")
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name == "sql")
                return TryGetConstantSqlText(argument.Value, out var sql) && HasOuterRowLimit(sql);
        }

        return false;
    }

    private static bool TryGetConstantSqlText(IOperation value, out string sql)
    {
        if (value.ConstantValue is { HasValue: true, Value: string constant })
        {
            sql = constant;
            return true;
        }

        sql = string.Empty;
        if (value.UnwrapConversions() is not IInterpolatedStringOperation interpolated)
            return false;

        var builder = new StringBuilder();
        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } }:
                    builder.Append(text);
                    break;
                case IInterpolationOperation:
                    // Each hole becomes a SQL parameter.
                    builder.Append(" {0} ");
                    break;
                default:
                    return false;
            }
        }

        sql = builder.ToString();
        return true;
    }

    private static bool HasOuterRowLimit(string sql)
    {
        var tokens = TokenizeOuterSql(sql);
        if (tokens == null)
            return false;

        var limited = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case "UNION" or "INTERSECT" or "EXCEPT" or "MINUS":
                    return false;

                case "LIMIT" when IsRowCount(tokens, i + 1, allowParen: false):
                    limited = true;
                    break;

                case "TOP" when IsRowCount(tokens, i + 1, allowParen: true) && TokenAt(tokens, i + 2) != "PERCENT" &&
                                !IsWithTies(tokens, i + 2):
                    limited = true;
                    break;

                case "FETCH" when TokenAt(tokens, i + 1) is "FIRST" or "NEXT":
                {
                    var next = i + 2;
                    if (IsRowCount(tokens, next, allowParen: true))
                        next++;

                    if (TokenAt(tokens, next) is "ROW" or "ROWS" && !IsWithTies(tokens, next + 1))
                        limited = true;

                    break;
                }
            }
        }

        return limited;
    }

    // WITH TIES adds every row that ties with the last one, which can be the whole table.
    private static bool IsWithTies(List<string> tokens, int index) =>
        TokenAt(tokens, index) == "WITH" && TokenAt(tokens, index + 1) == "TIES";

    private static string? TokenAt(List<string> tokens, int index) =>
        index < tokens.Count ? tokens[index] : null;

    private static bool IsRowCount(List<string> tokens, int index, bool allowParen) =>
        TokenAt(tokens, index) is NumberToken or ParameterToken ||
        allowParen && TokenAt(tokens, index) == CountGroupToken;

    // (10), (@n), ({0}) or ($1): a parenthesized count that is a single number or parameter, not an expression.
    private static bool IsSimpleCountGroup(string sql, int open)
    {
        var close = sql.IndexOf(')', open + 1);
        if (close < 0)
            return false;

        var content = sql.Substring(open + 1, close - open - 1).Trim();
        if (content.Length == 0)
            return false;

        var start = 0;
        if (content[0] is '@' or ':' or '$' or '?')
            start = 1;
        else if (content[0] == '{' && content[content.Length - 1] == '}')
            content = content.Substring(1, content.Length - 2);

        if (start == content.Length)
            return content == "?";

        for (var i = start; i < content.Length; i++)
        {
            if (start == 0 ? !char.IsDigit(content[i]) : !IsWordChar(content[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Upper-cased words, numbers (<c>#</c>), parameters (<c>?</c>) and parenthesized groups (<c>(</c>) at nesting depth
    /// zero. Comments and quoted literals or identifiers are skipped. Returns null for unbalanced text.
    /// </summary>
    private static List<string>? TokenizeOuterSql(string sql)
    {
        var tokens = new List<string>();
        var depth = 0;
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            // -- line comment, or a MySQL # line comment. A SQL Server #Temp table name reads the same, so everything
            // after a # is ignored; a limit written after one does not count.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-' || c == '#')
            {
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                // SQL Server and PostgreSQL nest block comments. MySQL does not, but reading them as nested only
                // hides more text, so a limit in the ambiguous part does not count.
                var commentDepth = 1;
                i += 2;
                while (commentDepth > 0)
                {
                    if (i + 1 >= sql.Length)
                        return null;
                    if (sql[i] == '/' && sql[i + 1] == '*')
                    {
                        commentDepth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && sql[i + 1] == '/')
                    {
                        commentDepth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                continue;
            }

            if (c is '\'' or '"' or '`' or '[')
            {
                var close = c == '[' ? ']' : c;
                i++;
                while (true)
                {
                    if (i >= sql.Length)
                        return null;
                    if (sql[i] == close)
                    {
                        // A doubled quote is an escaped quote inside the literal.
                        if (i + 1 < sql.Length && sql[i + 1] == close)
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }

                i++;
                if (depth == 0)
                    tokens.Add("'");
                continue;
            }

            if (c == '(')
            {
                if (depth == 0)
                    tokens.Add(IsSimpleCountGroup(sql, i) ? CountGroupToken : OpenParenToken);
                depth++;
                i++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth < 0)
                    return null;
                i++;
                continue;
            }

            if (c == '$' && TryGetDollarQuoteTag(sql, i, out var tag))
            {
                // PostgreSQL dollar-quoted literal: $$...$$ or $tag$...$tag$.
                var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                if (end < 0)
                    return null;
                i = end + tag.Length;
                if (depth == 0)
                    tokens.Add("'");
                continue;
            }

            var start = i;
            if (c is '@' or ':' or '$' or '{' or '?')
            {
                i++;
                if (c == '{')
                {
                    while (i < sql.Length && sql[i] != '}')
                        i++;
                    i++;
                }
                else
                {
                    while (i < sql.Length && IsWordChar(sql[i]))
                        i++;
                }

                if (depth == 0)
                    tokens.Add(ParameterToken);
                continue;
            }

            if (char.IsDigit(c))
            {
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.'))
                    i++;
                if (depth == 0)
                    tokens.Add(NumberToken);
                continue;
            }

            if (IsWordChar(c))
            {
                while (i < sql.Length && IsWordChar(sql[i]))
                    i++;
                if (depth == 0)
                    tokens.Add(sql.Substring(start, i - start).ToUpperInvariant());
                continue;
            }

            if (!char.IsWhiteSpace(c) && depth == 0)
                tokens.Add(c.ToString());
            i++;
        }

        return depth == 0 ? tokens : null;
    }

    private static bool TryGetDollarQuoteTag(string sql, int start, out string tag)
    {
        tag = "";
        var i = start + 1;
        if (i < sql.Length && char.IsDigit(sql[i]))
            return false; // $1 is a positional parameter.

        while (i < sql.Length && IsWordChar(sql[i]))
            i++;

        if (i >= sql.Length || sql[i] != '$')
            return false;

        tag = sql.Substring(start, i - start + 1);
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
