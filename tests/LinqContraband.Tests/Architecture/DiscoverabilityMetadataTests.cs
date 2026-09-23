using System.Text.RegularExpressions;
using System.Xml.Linq;
using LinqContraband.Catalog;

namespace LinqContraband.Tests.Architecture;

/// <summary>
/// Guards NuGet/GitHub discoverability assets: package description/tags, README funnel,
/// and product-flow visuals that ship with PackageReadmeFile.
/// </summary>
public sealed class DiscoverabilityMetadataTests
{
    private static string RepositoryRoot => RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void Analyzer_package_description_and_tags_include_high_intent_ef_core_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "LinqContraband", "LinqContraband.csproj"));

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var version = Assert.Single(csproj.Descendants("Version")).Value;
        var readmeFile = Assert.Single(csproj.Descendants("PackageReadmeFile")).Value;

        Assert.Equal("README.md", readmeFile);
        Assert.Contains("EF Core", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LINQ", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analyzer", title, StringComparison.OrdinalIgnoreCase);

        foreach (var term in new[]
                 {
                     "EF Core",
                     "LINQ",
                     "Roslyn",
                     "N+1",
                     "client-side evaluation",
                     "AsNoTracking",
                     "DbContext",
                     "Compile-time",
                 })
        {
            Assert.True(
                description.Contains(term, StringComparison.Ordinal),
                $"Analyzer Description must contain '{term}' for NuGet search discoverability.");
        }

        foreach (var tag in new[]
                 {
                     "efcore",
                     "entity-framework-core",
                     "EntityFrameworkCore",
                     "LINQ",
                     "IQueryable",
                     "NPlusOne",
                     "client-side-evaluation",
                     "AsNoTracking",
                     "DbContext",
                     "query-performance",
                     "roslyn",
                     "roslyn-analyzer",
                     "analyzers",
                 })
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Analyzer PackageTags must include '{tag}'.");
        }

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public void Readme_conversion_funnel_and_product_visuals_exist_with_resolvable_paths()
    {
        var readmePath = Path.Combine(RepositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);
        var version = Assert.Single(
            XDocument.Load(Path.Combine(RepositoryRoot, "src", "LinqContraband", "LinqContraband.csproj"))
                .Descendants("Version")).Value;

        foreach (var section in new[]
                 {
                     "## The problem",
                     "## What it catches",
                     "## Install",
                     "## See it work",
                     "## 30-second path",
                     "## Feature snapshot",
                     "## Rules",
                     "## Configuration",
                     "## Run it in CI",
                 })
        {
            Assert.Contains(section, readme, StringComparison.Ordinal);
        }

        Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);
        // Install snippets must not pin the current version; they go stale on every release.
        Assert.Contains("dotnet add package LinqContraband", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"Version=\"{version}\"", readme, StringComparison.Ordinal);
        Assert.DoesNotContain($"--version {version}", readme, StringComparison.Ordinal);
        Assert.Contains("LC001", readme, StringComparison.Ordinal);
        Assert.Contains("LC046", readme, StringComparison.Ordinal);
        Assert.Contains("LC047", readme, StringComparison.Ordinal);
        Assert.Contains("stays quiet", readme, StringComparison.OrdinalIgnoreCase);

        // NuGet.org requires absolute HTTPS image URLs in PackageReadmeFile content.
        // GitHub raw URLs keep both NuGet and GitHub README rendering working.
        const string rawBase =
            "https://raw.githubusercontent.com/georgepwall1991/LinqContraband/master/";

        var visualAssets = new[]
        {
            "assets/flow-ide-diagnostics.svg",
            "assets/flow-before-after-fix.svg",
            "assets/flow-analyzer-ci-loop.svg",
        };

        foreach (var asset in visualAssets)
        {
            Assert.Contains(rawBase + asset, readme, StringComparison.Ordinal);
            var fullPath = Path.Combine(RepositoryRoot, asset);
            Assert.True(File.Exists(fullPath), $"Missing README visual: {asset}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Empty README visual: {asset}");
        }

        Assert.Contains(rawBase + "icon.png", readme, StringComparison.Ordinal);

        // Relative image paths break NuGet.org README rendering — require HTTPS.
        var imageRefs = Regex.Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
            .Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(readme, @"<img[^>]+src=""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

        foreach (var imageRef in imageRefs)
        {
            Assert.True(
                imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"README image must use absolute HTTPS for NuGet rendering: {imageRef}");
        }
    }

    [Fact]
    public void Rule_pages_have_search_ready_titles_and_unique_meta_descriptions()
    {
        // The docs layout renders `description` as the meta description and page intro; without it
        // every rule page falls back to the same site-wide description in search results.
        var failures = new List<string>();
        var seenDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in RuleCatalog.All)
        {
            var sourcePath = Path.Combine(RepositoryRoot, rule.DocumentationPath);
            var frontMatter = File.ReadLines(sourcePath)
                .Skip(1)
                .TakeWhile(line => line.Trim() != "---")
                .ToArray();

            var title = ReadFrontMatterValue(frontMatter, "title");
            var description = ReadFrontMatterValue(frontMatter, "description");

            if (title is null || !title.StartsWith(rule.Id + ": ", StringComparison.Ordinal))
                failures.Add($"{rule.Id}: title should read \"{rule.Id}: <name>\" but was \"{title}\".");

            if (description is null)
            {
                failures.Add($"{rule.Id}: {rule.DocumentationPath} needs a description in its front matter.");
                continue;
            }

            if (description.Length is < 70 or > 160)
                failures.Add($"{rule.Id}: description is {description.Length} characters; keep it between 70 and 160 so search results show it whole.");
            if (!description.Contains(rule.Id, StringComparison.Ordinal))
                failures.Add($"{rule.Id}: description should name the rule id.");
            if (seenDescriptions.TryGetValue(description, out var other))
                failures.Add($"{rule.Id}: description duplicates {other}.");
            else
                seenDescriptions[description] = rule.Id;
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Docs_pages_take_the_rule_count_from_site_data()
    {
        // New rules land often, so a typed-in count ("47 rules") goes stale on the next release.
        // Pages use {{ site.data.rules | size }} instead; front matter cannot, so it names no count.
        var countPattern = new Regex(
            @"\b[1-9][0-9] (?:[A-Za-z]+ ){0,3}(?:rules|diagnostics|analy[sz]ers)\b",
            RegexOptions.IgnoreCase);
        var docsRoot = Path.Combine(RepositoryRoot, "docs");
        var failures = new List<string>();

        foreach (var path in Directory.EnumerateFiles(docsRoot, "*.md").Concat(Directory.EnumerateFiles(docsRoot, "*.html")))
        {
            // The health doc is an audit log; its counts describe past releases on purpose.
            if (Path.GetFileName(path) == "analyzer-health.md")
                continue;

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = countPattern.Match(lines[index]);
                if (match.Success)
                    failures.Add($"docs/{Path.GetFileName(path)}:{index + 1}: \"{match.Value}\" should use {{{{ site.data.rules | size }}}} or drop the number.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string? ReadFrontMatterValue(IEnumerable<string> frontMatter, string key)
    {
        var line = frontMatter.FirstOrDefault(candidate => candidate.StartsWith(key + ":", StringComparison.Ordinal));
        return line?.Substring(key.Length + 1).Trim().Trim('"');
    }

    [Fact]
    public void Analyzer_packs_all_assets_for_nuget_readme_rendering()
    {
        var analyzer = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "LinqContraband", "LinqContraband.csproj"));

        Assert.Contains(
            analyzer.Descendants("None"),
            n => (n.Attribute("Include")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal)
                && string.Equals(n.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal));
    }
}
