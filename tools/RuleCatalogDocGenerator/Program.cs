using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LinqContraband.Catalog;

const string ReadmeTableStartMarker = "<!-- rule-table:start (generated from RuleCatalog by tools/RuleCatalogDocGenerator; do not edit by hand) -->";
const string ReadmeTableEndMarker = "<!-- rule-table:end -->";
const string WriteCommand = "dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write";

var repoRoot = FindRepoRoot();
var catalogPath = Path.Combine(repoRoot, "docs", "rule-catalog.md");
var readmePath = Path.Combine(repoRoot, "README.md");
var rulesDataPath = Path.Combine(repoRoot, "docs", "_data", "rules.json");

var checkOnly = args.Contains("--check", StringComparer.Ordinal);
var writeOnly = args.Contains("--write", StringComparer.Ordinal);

if (checkOnly && writeOnly)
{
    Console.Error.WriteLine("Use either --check or --write, not both.");
    return 1;
}

// Compare with line endings normalized to LF: the generated text is canonical LF, but a
// Windows (autocrlf) checkout stores the files with CRLF, so a raw byte comparison would
// report a spurious "out of date" on Windows even when the content is identical.
var currentCatalog = File.Exists(catalogPath) ? NormalizeNewlines(File.ReadAllText(catalogPath)) : string.Empty;
var generatedCatalog = GenerateMarkdown();

var currentReadme = NormalizeNewlines(File.ReadAllText(readmePath));
if (!TryReplaceReadmeRuleTable(currentReadme, GenerateReadmeRuleTable(), out var generatedReadme))
{
    Console.Error.WriteLine($"{readmePath} must contain a single '{ReadmeTableStartMarker}' ... '{ReadmeTableEndMarker}' block.");
    return 1;
}

var buildDirectory = Path.Combine(repoRoot, "src", "LinqContraband", "build");
var presetOutputs = RuleCatalogPresets.All
    .Select(preset => (Path: Path.Combine(buildDirectory, "presets", preset.FileName), Name: "src/LinqContraband/build/presets/" + preset.FileName, Generated: preset.ToGlobalConfig()))
    .Append((Path: Path.Combine(buildDirectory, "LinqContraband.targets"), Name: "src/LinqContraband/build/LinqContraband.targets", Generated: GeneratePresetTargets()))
    .Select(output => (output.Path, output.Name, Current: File.Exists(output.Path) ? NormalizeNewlines(File.ReadAllText(output.Path)) : string.Empty, output.Generated));

var currentRulesData = File.Exists(rulesDataPath) ? NormalizeNewlines(File.ReadAllText(rulesDataPath)) : string.Empty;
var generatedRulesData = GenerateRulesData();

var outputs = new[]
{
    (Path: catalogPath, Name: "docs/rule-catalog.md", Current: currentCatalog, Generated: generatedCatalog),
    (Path: readmePath, Name: "README.md rule table", Current: currentReadme, Generated: generatedReadme),
    (Path: rulesDataPath, Name: "docs/_data/rules.json", Current: currentRulesData, Generated: generatedRulesData),
}.Concat(presetOutputs).ToArray();

if (checkOnly)
{
    var stale = outputs.Where(output => !string.Equals(output.Current, output.Generated, StringComparison.Ordinal)).ToArray();
    foreach (var output in stale)
        Console.Error.WriteLine($"{output.Name} is out of date. Run: {WriteCommand}");

    if (stale.Length > 0)
        return 1;

    Console.WriteLine("docs/rule-catalog.md, docs/_data/rules.json, the README.md rule table, and the severity presets are up to date.");
    return 0;
}

foreach (var output in outputs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(output.Path)!);
    File.WriteAllText(output.Path, output.Generated, new UTF8Encoding(false));
    Console.WriteLine($"Wrote {output.Path}");
}

return 0;

static string GeneratePresetTargets()
{
    var names = RuleCatalogPresets.All.Select(preset => preset.Name).ToArray();
    var builder = new StringBuilder();
    builder.Append("<Project>\n");
    builder.Append("  <!-- Generated from RuleCatalogPresets by tools/RuleCatalogDocGenerator; do not edit by hand.\n");
    builder.Append("       Opt-in severity presets: set <LinqContrabandPreset> to one or more of ").Append(string.Join(", ", names)).Append("\n");
    builder.Append("       (separated by ';' or ','). Imported as a .targets file so the property can be set anywhere in the project.\n");
    builder.Append("       The presets are global analyzer configs (is_global = true); they go straight into EditorConfigFiles because\n");
    builder.Append("       Roslyn copies GlobalAnalyzerConfigFiles into EditorConfigFiles before package targets are imported. -->\n");
    builder.Append("  <PropertyGroup>\n");
    builder.Append("    <_LinqContrabandPresetList>;$([System.String]::Copy('$(LinqContrabandPreset)').ToLowerInvariant().Replace(',', ';').Replace(' ', ''));</_LinqContrabandPresetList>\n");
    builder.Append("  </PropertyGroup>\n");
    builder.Append("\n");
    builder.Append("  <ItemGroup>\n");
    foreach (var preset in RuleCatalogPresets.All)
    {
        builder.Append("    <EditorConfigFiles Include=\"$(MSBuildThisFileDirectory)presets/").Append(preset.FileName)
            .Append("\" Condition=\"$(_LinqContrabandPresetList.Contains(';").Append(preset.Name).Append(";'))\" />\n");
    }

    builder.Append("  </ItemGroup>\n");
    builder.Append("\n");
    builder.Append("  <Target Name=\"_LinqContrabandValidatePreset\" BeforeTargets=\"CoreCompile\" Condition=\"'$(LinqContrabandPreset)' != ''\">\n");
    builder.Append("    <PropertyGroup>\n");
    builder.Append("      <_LinqContrabandUnknownPresets>$(_LinqContrabandPresetList)</_LinqContrabandUnknownPresets>\n");
    foreach (var name in names)
        builder.Append("      <_LinqContrabandUnknownPresets>$(_LinqContrabandUnknownPresets.Replace(';").Append(name).Append(";', ';'))</_LinqContrabandUnknownPresets>\n");
    builder.Append("      <_LinqContrabandUnknownPresets>$(_LinqContrabandUnknownPresets.Trim(';'))</_LinqContrabandUnknownPresets>\n");
    builder.Append("    </PropertyGroup>\n");
    builder.Append("    <Warning Code=\"LCPRESET\" Condition=\"'$(_LinqContrabandUnknownPresets)' != ''\" Text=\"Unknown LinqContrabandPreset '$(_LinqContrabandUnknownPresets)'. Valid presets: ").Append(string.Join(", ", names)).Append(".\" />\n");
    builder.Append("  </Target>\n");
    builder.Append("</Project>\n");
    return builder.ToString();
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "LinqContraband.sln")))
            return current.FullName;

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate LinqContraband.sln from the current working directory.");
}

static string GenerateMarkdown()
{
    var builder = new StringBuilder();
    builder.AppendLine("---");
    builder.AppendLine("layout: default");
    builder.AppendLine("title: LinqContraband Rule Catalog");
    builder.AppendLine("description: Full LinqContraband EF Core analyzer rule catalog grouped by query, materialization, loading, async, tracking, raw SQL, and schema design.");
    builder.AppendLine("permalink: /rule-catalog.html");
    builder.AppendLine("body_class: page-rule-catalog");
    builder.AppendLine("---");
    builder.AppendLine();
    var rules = RuleCatalog.All
        .OrderBy(rule => rule.Domain, StringComparer.Ordinal)
        .ThenBy(rule => rule.Id, StringComparer.Ordinal)
        .ToArray();

    var warningCount = rules.Count(rule => rule.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    var codeFixCount = rules.Count(rule => rule.HasCodeFix);
    var domainCount = rules.Select(rule => rule.Domain).Distinct(StringComparer.Ordinal).Count();

    builder.AppendLine("<section class=\"catalog-intro\">");
    builder.AppendLine("  <div class=\"catalog-intro__copy\">");
    builder.AppendLine("    <p>The source of truth for rule metadata lives in <code>src/LinqContraband/Catalog/RuleCatalog.cs</code>. This page is generated from that catalog and grouped by EF Core failure mode.</p>");
    builder.AppendLine("  </div>");
    builder.AppendLine("  <div class=\"metric-strip\" aria-label=\"Rule catalog summary\">");
    builder.AppendLine($"    <div class=\"metric\"><strong>{rules.Length}</strong><span>rules</span></div>");
    builder.AppendLine($"    <div class=\"metric\"><strong>{warningCount}</strong><span>warnings</span></div>");
    builder.AppendLine($"    <div class=\"metric\"><strong>{codeFixCount}</strong><span>code fixes</span></div>");
    builder.AppendLine("  </div>");
    builder.AppendLine("</section>");
    builder.AppendLine();
    builder.AppendLine($"<p class=\"eyebrow\">{domainCount} diagnostic domains</p>");
    builder.AppendLine();

    var groups = rules
        .GroupBy(rule => rule.Domain, StringComparer.Ordinal);

    foreach (var group in groups)
    {
        var domainId = ToToken(group.Key);

        builder.AppendLine($"<section class=\"rule-domain\" aria-labelledby=\"{domainId}\">");
        builder.AppendLine("  <div class=\"rule-domain__heading\">");
        builder.AppendLine($"    <h2 id=\"{domainId}\">{Encode(group.Key)}</h2>");
        builder.AppendLine($"    <p>{Encode(GetDomainDescription(group.Key))}</p>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <div class=\"rule-grid\">");

        foreach (var rule in group)
        {
            var fixText = rule.HasCodeFix ? "Code fix" : "Manual only";
            var fixClass = rule.HasCodeFix ? "fix" : "manual";
            var docsFileName = Path.ChangeExtension(Path.GetFileName(rule.DocumentationPath), ".html");
            var sampleDirectory = Path.GetDirectoryName(rule.SamplePath)?.Replace('\\', '/');
            var shortSampleDirectory = sampleDirectory is null
                ? rule.SamplePath.Replace('\\', '/')
                : sampleDirectory.Replace("samples/LinqContraband.Sample/", string.Empty, StringComparison.Ordinal);

            builder.AppendLine($"    <a class=\"rule-card\" href=\"./{EncodeAttribute(docsFileName)}\">");
            builder.AppendLine("      <span class=\"rule-card__top\">");
            builder.AppendLine($"        <span class=\"rule-card__id\">{Encode(rule.Id)}</span>");
            builder.AppendLine($"        <span class=\"pill pill--{ToToken(rule.Severity.ToString())}\">{Encode(rule.Severity.ToString())}</span>");
            builder.AppendLine("      </span>");
            builder.AppendLine($"      <h3>{Encode(rule.Title)}</h3>");
            builder.AppendLine("      <span class=\"rule-card__meta\">");
            builder.AppendLine($"        <span>{Encode(rule.Category)}</span>");
            builder.AppendLine($"        <span class=\"pill pill--{fixClass}\">{Encode(fixText)}</span>");
            builder.AppendLine("      </span>");
            builder.AppendLine($"      <span class=\"rule-card__sample\">{Encode(shortSampleDirectory)}/</span>");
            builder.AppendLine("    </a>");
        }

        builder.AppendLine("  </div>");
        builder.AppendLine("</section>");
        builder.AppendLine();
    }

    // StringBuilder.AppendLine emits Environment.NewLine (CRLF on Windows); normalize the whole
    // document to LF so generation is byte-identical on every platform and matches git.
    return NormalizeNewlines(builder.ToString());
}

static string GenerateReadmeRuleTable()
{
    // The README is also the NuGet package readme, so every link must be absolute.
    var rules = RuleCatalog.All
        .OrderBy(rule => rule.Id, StringComparer.Ordinal)
        .ToArray();
    var codeFixCount = rules.Count(rule => rule.HasCodeFix);

    var builder = new StringBuilder();
    builder.AppendLine($"**{rules.Length} rules**, {codeFixCount} with automatic code fixes. Each rule links to its full page: what it flags, why it matters, how to fix it, and where it deliberately stays quiet.");
    builder.AppendLine();
    builder.AppendLine("| Rule | What it catches | Default severity | Code fix |");
    builder.AppendLine("| --- | --- | --- | --- |");

    foreach (var rule in rules)
    {
        var fixText = rule.HasCodeFix ? "Yes" : "Manual";
        builder.AppendLine($"| [{rule.Id}]({rule.HelpLinkUri}) | {EscapeTableCell(rule.Title)} | {rule.Severity} | {fixText} |");
    }

    return NormalizeNewlines(builder.ToString());
}

static string GenerateRulesData()
{
    // Rule pages on the docs site read this file (site.data.rules) to show each rule's default
    // severity, code-fix availability and configuration snippet straight from the catalog.
    const string repositoryBlobUri = "https://github.com/georgepwall1991/LinqContraband/blob/master/";
    const string repositoryTreeUri = "https://github.com/georgepwall1991/LinqContraband/tree/master/";

    var rules = RuleCatalog.All
        .OrderBy(rule => rule.Id, StringComparer.Ordinal)
        .Select(rule => new Dictionary<string, object>
        {
            ["id"] = rule.Id,
            ["title"] = rule.Title,
            ["category"] = rule.Category,
            ["domain"] = rule.Domain,
            ["domain_anchor"] = ToToken(rule.Domain),
            ["severity"] = rule.Severity.ToString(),
            ["code_fix"] = rule.HasCodeFix,
            ["sample_url"] = repositoryBlobUri + rule.SamplePath.Replace('\\', '/'),
            ["source_url"] = repositoryTreeUri + rule.AnalyzerSourcePath.Replace('\\', '/'),
        })
        .ToArray();

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    return NormalizeNewlines(JsonSerializer.Serialize(rules, options)) + "\n";
}

static bool TryReplaceReadmeRuleTable(string readme, string table, out string updated)
{
    updated = readme;
    var start = readme.IndexOf(ReadmeTableStartMarker, StringComparison.Ordinal);
    var end = readme.IndexOf(ReadmeTableEndMarker, StringComparison.Ordinal);
    if (start < 0 || end < start ||
        readme.IndexOf(ReadmeTableStartMarker, start + 1, StringComparison.Ordinal) >= 0 ||
        readme.IndexOf(ReadmeTableEndMarker, end + 1, StringComparison.Ordinal) >= 0)
    {
        return false;
    }

    var contentStart = start + ReadmeTableStartMarker.Length;
    // Blank lines around the table keep the markers from merging into the table in any renderer.
    updated = readme.Substring(0, contentStart) + "\n\n" + table + "\n" + readme.Substring(end);
    return true;
}

static string EscapeTableCell(string value)
{
    return value.Replace("|", "\\|", StringComparison.Ordinal);
}

static string NormalizeNewlines(string value)
{
    return value.Replace("\r\n", "\n").Replace("\r", "\n");
}

static string Encode(string value)
{
    return WebUtility.HtmlEncode(value);
}

static string EncodeAttribute(string value)
{
    return WebUtility.HtmlEncode(value);
}

static string GetDomainDescription(string domain)
{
    return domain switch
    {
        "Bulk Operations & Set-Based Writes" => "Keep destructive and high-volume writes set-based while making the risky cases explicit.",
        "Change Tracking & Context Lifetime" => "Spot DbContext lifetime leaks, tracking-mode surprises, and writes that silently do nothing.",
        "Execution & Async" => "Find synchronous calls, repeated database execution, and async paths that drop cancellation or buffer too early.",
        "Loading & Includes" => "Make relationship loading deliberate before N+1 round trips or over-eager include graphs reach production.",
        "Materialization & Projection" => "Keep work in SQL where it belongs and avoid loading whole entities or unbounded result sets by accident.",
        "Query Shape & Translation" => "Catch LINQ patterns that EF Core cannot translate reliably or cannot page deterministically.",
        "Raw SQL & Security" => "Flag SQL construction patterns that can bypass parameterization, tenant filters, or review expectations.",
        "Schema & Modeling" => "Guard model shape choices that produce fragile entity mappings and unclear relationships.",
        _ => "Review the EF Core query and model shapes covered by this diagnostic group."
    };
}

static string ToToken(string value)
{
    var builder = new StringBuilder(value.Length);
    var previousWasDash = false;

    foreach (var character in value)
    {
        if (char.IsLetterOrDigit(character))
        {
            builder.Append(char.ToLowerInvariant(character));
            previousWasDash = false;
        }
        else if (!previousWasDash)
        {
            builder.Append('-');
            previousWasDash = true;
        }
    }

    return builder.ToString().Trim('-');
}
