using System.Net;
using System.Text;
using LinqContraband.Catalog;

var repoRoot = FindRepoRoot();
var outputPath = Path.Combine(repoRoot, "docs", "rule-catalog.md");

var checkOnly = args.Contains("--check", StringComparer.Ordinal);
var writeOnly = args.Contains("--write", StringComparer.Ordinal);

if (checkOnly && writeOnly)
{
    Console.Error.WriteLine("Use either --check or --write, not both.");
    return 1;
}

var generated = GenerateMarkdown();

if (checkOnly)
{
    // Compare with line endings normalized to LF: the generated text is canonical LF, but a
    // Windows (autocrlf) checkout stores docs/rule-catalog.md with CRLF, so a raw byte comparison
    // would report a spurious "out of date" on Windows even when the content is identical.
    var current = File.Exists(outputPath) ? NormalizeNewlines(File.ReadAllText(outputPath)) : string.Empty;
    if (!string.Equals(current, generated, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"{outputPath} is out of date. Run: dotnet run --project tools/RuleCatalogDocGenerator/RuleCatalogDocGenerator.csproj -- --write");
        return 1;
    }

    Console.WriteLine("docs/rule-catalog.md is up to date.");
    return 0;
}

File.WriteAllText(outputPath, generated, new UTF8Encoding(false));
Console.WriteLine($"Wrote {outputPath}");
return 0;

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
