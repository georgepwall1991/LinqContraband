using System.Text.Json;
using System.Text.RegularExpressions;

namespace LinqContraband.Scan;

internal sealed record RuleInfo(string Id, string Title, string Severity, string? HelpUri, string? Description = null, string? Category = null);

internal sealed record Finding(string RuleId, string Severity, string Message, string Path, int Line, int Column);

/// <summary>Reads LinqContraband results out of the SARIF 2.1 error logs the C# compiler writes.</summary>
internal static partial class SarifReader
{
    /// <summary>
    /// LinqContraband's rules, plus EF Core's EF1002 and EF1003: LC018 and LC034 stay quiet on the raw SQL calls
    /// those report, so leaving them out would drop SQL injection findings from the scan on EF Core 8 and later.
    /// </summary>
    [GeneratedRegex(@"^(LC\d{3}|EF100[23])$")]
    private static partial Regex RuleIdPattern();

    /// <summary>EF Core's analyzers publish no help link; this page covers both of its raw SQL diagnostics.</summary>
    public const string EfCoreSqlQueriesUri = "https://learn.microsoft.com/ef/core/querying/sql-queries";

    public static bool IsReportedRule(string? ruleId) => ruleId is not null && RuleIdPattern().IsMatch(ruleId);

    /// <summary>
    /// Adds the unsuppressed LCxxx, EF1002 and EF1003 results in <paramref name="json"/> to <paramref name="findings"/> and their rule
    /// metadata to <paramref name="rules"/>. Results the code suppressed with <c>#pragma</c> or
    /// <c>[SuppressMessage]</c> are skipped, matching what the build reports.
    /// </summary>
    public static void Read(string json, ICollection<Finding> findings, IDictionary<string, RuleInfo> rules)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            return;

        foreach (var run in runs.EnumerateArray())
        {
            ReadRules(run, rules);

            if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var result in results.EnumerateArray())
            {
                var ruleId = GetString(result, "ruleId");
                if (!IsReportedRule(ruleId) || IsSuppressed(result))
                    continue;

                if (!TryGetLocation(result, out var path, out var line, out var column))
                    continue;

                var severity = ToSeverity(GetString(result, "level"));
                var message = result.TryGetProperty("message", out var messageElement) ? GetString(messageElement, "text") ?? "" : "";
                findings.Add(new Finding(ruleId!, severity, message, path, line, column));
            }
        }
    }

    private static void ReadRules(JsonElement run, IDictionary<string, RuleInfo> rules)
    {
        if (!run.TryGetProperty("tool", out var tool) ||
            !tool.TryGetProperty("driver", out var driver) ||
            !driver.TryGetProperty("rules", out var ruleArray) ||
            ruleArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var rule in ruleArray.EnumerateArray())
        {
            var id = GetString(rule, "id");
            if (!IsReportedRule(id) || rules.ContainsKey(id!))
                continue;

            var title = rule.TryGetProperty("shortDescription", out var shortDescription) ? GetString(shortDescription, "text") : null;
            var level = rule.TryGetProperty("defaultConfiguration", out var configuration) ? GetString(configuration, "level") : null;
            var helpUri = GetString(rule, "helpUri") ?? (id!.StartsWith("EF", StringComparison.Ordinal) ? EfCoreSqlQueriesUri : null);
            var description = rule.TryGetProperty("fullDescription", out var fullDescription) ? GetString(fullDescription, "text") : null;
            var category = rule.TryGetProperty("properties", out var properties) ? GetString(properties, "category") : null;
            rules[id!] = new RuleInfo(id!, title ?? id!, ToSeverity(level), helpUri, string.IsNullOrWhiteSpace(description) ? null : description, category);
        }
    }

    private static bool IsSuppressed(JsonElement result) =>
        result.TryGetProperty("suppressions", out var suppressions) &&
        suppressions.ValueKind == JsonValueKind.Array &&
        suppressions.GetArrayLength() > 0;

    private static bool TryGetLocation(JsonElement result, out string path, out int line, out int column)
    {
        path = "";
        line = 0;
        column = 0;

        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array ||
            locations.GetArrayLength() == 0 ||
            !locations[0].TryGetProperty("physicalLocation", out var physical) ||
            !physical.TryGetProperty("artifactLocation", out var artifact))
        {
            return false;
        }

        var uri = GetString(artifact, "uri");
        if (string.IsNullOrEmpty(uri))
            return false;

        path = Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : Uri.UnescapeDataString(uri);

        if (physical.TryGetProperty("region", out var region))
        {
            line = GetInt(region, "startLine");
            column = GetInt(region, "startColumn");
        }

        return true;
    }

    /// <summary>Maps a SARIF level to the severity name Roslyn and the docs use.</summary>
    internal static string ToSeverity(string? level) => level switch
    {
        "error" => "Error",
        "warning" => "Warning",
        "note" => "Info",
        "none" => "Hidden",
        _ => "Warning",
    };

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
