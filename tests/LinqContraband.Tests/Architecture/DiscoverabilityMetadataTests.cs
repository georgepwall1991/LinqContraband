using System.Text.RegularExpressions;
using System.Xml.Linq;

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
                     "## Rule Details",
                 })
        {
            Assert.Contains(section, readme, StringComparison.Ordinal);
        }

        Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);
        Assert.Contains($"Version=\"{version}\"", readme, StringComparison.Ordinal);
        Assert.Contains("LC001", readme, StringComparison.Ordinal);
        Assert.Contains("LC046", readme, StringComparison.Ordinal);
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
