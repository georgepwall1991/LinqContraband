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
    builder.AppendLine("---");
    builder.AppendLine();
    builder.AppendLine("# Rule Catalog");
    builder.AppendLine();
    builder.AppendLine("The source of truth for rule metadata lives in `src/LinqContraband/Catalog/RuleCatalog.cs`.");
    builder.AppendLine("This page is generated from that catalog and grouped by domain.");
    builder.AppendLine();

    var groups = RuleCatalog.All
        .OrderBy(rule => rule.Domain, StringComparer.Ordinal)
        .ThenBy(rule => rule.Id, StringComparer.Ordinal)
        .GroupBy(rule => rule.Domain, StringComparer.Ordinal);

    foreach (var group in groups)
    {
        builder.AppendLine($"## {group.Key}");
        builder.AppendLine();
        builder.AppendLine("| Rule | Severity | Legacy Category | Fix | Docs | Sample |\n| --- | --- | --- | --- | --- | --- |");

        foreach (var rule in group)
        {
            var fixText = rule.HasCodeFix ? "Code fix" : "Manual only";
            var docsFileName = Path.ChangeExtension(Path.GetFileName(rule.DocumentationPath), ".html");
            var docsLink = $"[`{rule.Slug}`](./{docsFileName})";
            var sampleDirectory = Path.GetDirectoryName(rule.SamplePath)?.Replace('\\', '/');
            var shortSampleDirectory = sampleDirectory is null
                ? rule.SamplePath.Replace('\\', '/')
                : sampleDirectory.Replace("samples/LinqContraband.Sample/", string.Empty, StringComparison.Ordinal);

            builder.AppendLine($"| `{rule.Id}` {EscapePipes(rule.Title)} | `{rule.Severity}` | `{rule.Category}` | {fixText} | {docsLink} | `{shortSampleDirectory}/` |");
        }

        builder.AppendLine();
    }

    // StringBuilder.AppendLine emits Environment.NewLine (CRLF on Windows) while line 67 embeds a
    // literal "\n"; normalize the whole document to LF so generation is byte-identical on every
    // platform and matches the LF form stored in git.
    return NormalizeNewlines(builder.ToString());
}

static string NormalizeNewlines(string value)
{
    return value.Replace("\r\n", "\n").Replace("\r", "\n");
}

static string EscapePipes(string value)
{
    return value.Replace("|", "\\|", StringComparison.Ordinal);
}
