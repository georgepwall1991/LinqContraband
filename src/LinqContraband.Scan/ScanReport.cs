using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LinqContraband.Scan;

internal sealed record RuleSummary(RuleInfo Rule, int Count, string Severity);

/// <summary>
/// The findings of one scan, deduplicated across target frameworks (a multi-targeted project compiles each
/// file once per framework) and with paths made relative to <see cref="RootDirectory"/> (the git work tree
/// around the scanned path, or the scanned directory itself).
/// </summary>
internal sealed class ScanReport
{
    public const string DocumentationSiteUri = "https://georgepwall1991.github.io/LinqContraband/";

    public const string RuleCatalogUri = DocumentationSiteUri + "rule-catalog.html";

    /// <summary>Rules beyond this many get one catalog link instead of a line each.</summary>
    private const int MaxRuleLinks = 10;

    private static readonly string[] SeverityOrder = ["Error", "Warning", "Info", "Hidden"];

    public ScanReport(string rootDirectory, IEnumerable<Finding> findings, IReadOnlyDictionary<string, RuleInfo> rules)
    {
        RootDirectory = rootDirectory;
        Findings = findings
            .Select(finding => finding with { Path = ToRelativePath(rootDirectory, finding.Path) })
            .DistinctBy(finding => (finding.RuleId, finding.Path, finding.Line, finding.Column))
            .OrderBy(finding => finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.Column)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ToList();
        Rules = rules;
    }

    public string RootDirectory { get; }

    public IReadOnlyList<Finding> Findings { get; }

    public IReadOnlyDictionary<string, RuleInfo> Rules { get; }

    /// <summary>Rules that reported, most severe first, then most frequent.</summary>
    public IReadOnlyList<RuleSummary> RuleSummaries() => Findings
        .GroupBy(finding => finding.RuleId, StringComparer.Ordinal)
        .Select(group =>
        {
            var severity = group.Select(finding => finding.Severity).OrderBy(SeverityRank).First();
            return new RuleSummary(GetRule(group.Key), group.Count(), severity);
        })
        .OrderBy(summary => SeverityRank(summary.Severity))
        .ThenByDescending(summary => summary.Count)
        .ThenBy(summary => summary.Rule.Id, StringComparer.Ordinal)
        .ToList();

    public IReadOnlyList<(string Path, int Count)> TopFiles(int count) => Findings
        .GroupBy(finding => finding.Path, StringComparer.Ordinal)
        .Select(group => (group.Key, group.Count()))
        .OrderByDescending(file => file.Item2)
        .ThenBy(file => file.Key, StringComparer.Ordinal)
        .Take(Math.Max(0, count))
        .ToList();

    public RuleInfo GetRule(string id) =>
        Rules.TryGetValue(id, out var rule) ? rule : new RuleInfo(id, id, "Warning", null);

    public string RenderText(int topFiles, string? sarifPath)
    {
        var text = new StringBuilder();
        var summaries = RuleSummaries();

        if (Findings.Count == 0)
        {
            text.AppendLine("LinqContraband found no EF Core query problems.");
        }
        else
        {
            var files = Findings.Select(finding => finding.Path).Distinct(StringComparer.Ordinal).Count();
            text.AppendLine(Invariant($"LinqContraband found {Plural(Findings.Count, "problem")} ({Plural(summaries.Count, "rule")}, {Plural(files, "file")})."));
            text.AppendLine();

            var titleWidth = Math.Min(60, summaries.Max(summary => summary.Rule.Title.Length));
            text.AppendLine(Invariant($"  {"Rule",-6} {"Severity",-8} {"Count",5}  Title"));
            foreach (var summary in summaries)
            {
                text.AppendLine(Invariant($"  {summary.Rule.Id,-6} {summary.Severity,-8} {summary.Count,5}  {Truncate(summary.Rule.Title, titleWidth)}"));
            }

            var top = TopFiles(topFiles);
            if (top.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Most affected files:");
                foreach (var (path, count) in top)
                    text.AppendLine(Invariant($"  {count,5}  {path}"));
            }

            text.AppendLine();
            text.AppendLine("What each rule means and how to fix it:");
            foreach (var summary in summaries.Take(MaxRuleLinks))
                text.AppendLine(Invariant($"  {summary.Rule.Id}  {summary.Rule.HelpUri ?? RuleCatalogUri}"));
            if (summaries.Count > MaxRuleLinks)
                text.AppendLine(Invariant($"  Every rule: {RuleCatalogUri}"));
        }

        if (sarifPath is not null)
        {
            text.AppendLine();
            text.AppendLine(Invariant($"SARIF report: {sarifPath}"));
        }

        text.AppendLine();
        text.AppendLine("Keep these checks on in the editor and CI: dotnet add package LinqContraband");
        return text.ToString();
    }

    /// <summary>
    /// Writes one SARIF 2.1.0 run holding every finding, with file locations relative to <see cref="RootDirectory"/>
    /// (the <c>%SRCROOT%</c> base), so it can be uploaded to GitHub code scanning as is.
    /// </summary>
    public void WriteSarif(Stream stream, string toolVersion)
    {
        var reportedRules = Findings.Select(finding => finding.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var ruleIndex = reportedRules.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
        writer.WriteString("version", "2.1.0");
        writer.WriteStartArray("runs");
        writer.WriteStartObject();

        writer.WriteStartObject("tool");
        writer.WriteStartObject("driver");
        writer.WriteString("name", "LinqContraband");
        writer.WriteString("version", toolVersion);
        writer.WriteString("semanticVersion", toolVersion);
        writer.WriteString("informationUri", DocumentationSiteUri);
        writer.WriteStartArray("rules");
        foreach (var id in reportedRules)
        {
            var rule = GetRule(id);
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WriteStartObject("shortDescription");
            writer.WriteString("text", rule.Title);
            writer.WriteEndObject();
            if (rule.HelpUri is not null)
                writer.WriteString("helpUri", rule.HelpUri);
            writer.WriteStartObject("defaultConfiguration");
            writer.WriteString("level", ToSarifLevel(rule.Severity));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartObject("originalUriBaseIds");
        writer.WriteStartObject("%SRCROOT%");
        writer.WriteString("uri", new Uri(EnsureTrailingSeparator(RootDirectory)).AbsoluteUri);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteStartArray("results");
        foreach (var finding in Findings)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", finding.RuleId);
            writer.WriteNumber("ruleIndex", ruleIndex[finding.RuleId]);
            writer.WriteString("level", ToSarifLevel(finding.Severity));
            writer.WriteStartObject("message");
            writer.WriteString("text", finding.Message);
            writer.WriteEndObject();
            writer.WriteStartArray("locations");
            writer.WriteStartObject();
            writer.WriteStartObject("physicalLocation");
            writer.WriteStartObject("artifactLocation");
            if (Path.IsPathRooted(finding.Path))
            {
                writer.WriteString("uri", new Uri(finding.Path).AbsoluteUri);
            }
            else
            {
                writer.WriteString("uri", string.Join('/', finding.Path.Split('/').Select(Uri.EscapeDataString)));
                writer.WriteString("uriBaseId", "%SRCROOT%");
            }
            writer.WriteEndObject();
            if (finding.Line > 0)
            {
                writer.WriteStartObject("region");
                writer.WriteNumber("startLine", finding.Line);
                if (finding.Column > 0)
                    writer.WriteNumber("startColumn", finding.Column);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Makes a path relative to the report root with forward slashes; paths outside it stay absolute.</summary>
    internal static string ToRelativePath(string rootDirectory, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(rootDirectory, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return fullPath;

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string directory) =>
        Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;

    private static int SeverityRank(string severity)
    {
        var index = Array.IndexOf(SeverityOrder, severity);
        return index < 0 ? SeverityOrder.Length : index;
    }

    private static string ToSarifLevel(string severity) => severity switch
    {
        "Error" => "error",
        "Warning" => "warning",
        "Info" => "note",
        _ => "none",
    };

    private static string Plural(int count, string noun) => Invariant($"{count} {noun}{(count == 1 ? "" : "s")}");

    private static string Truncate(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
