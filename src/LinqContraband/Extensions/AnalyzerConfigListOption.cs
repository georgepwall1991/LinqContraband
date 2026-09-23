using System;
using System.Collections.Generic;

namespace LinqContraband.Extensions;

/// <summary>
/// Splits a list-valued <c>dotnet_code_quality.LCxxx.*</c> option. The .editorconfig and .globalconfig
/// parsers treat <c>;</c> and <c>#</c> as the start of an inline comment, so everything after the first
/// <c>;</c> never reaches the analyzer; commas are the documented separator. <c>;</c> is still accepted
/// for values that arrive intact through other hosts.
/// </summary>
internal static class AnalyzerConfigListOption
{
    private static readonly char[] Separators = { ',', ';' };

    public static List<string> Split(string value)
    {
        var entries = new List<string>();
        foreach (var entry in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();
            if (trimmed.Length > 0)
                entries.Add(trimmed);
        }

        return entries;
    }
}
