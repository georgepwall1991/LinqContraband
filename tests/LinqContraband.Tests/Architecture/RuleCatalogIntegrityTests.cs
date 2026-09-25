using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LinqContraband.Catalog;
using Xunit;

namespace LinqContraband.Tests.Architecture;

public sealed class RuleCatalogIntegrityTests
{
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void RuleCatalog_IsUnique_Ordered_AndComplete()
    {
        var rules = RuleCatalog.All;

        Assert.Equal(59, rules.Length);
        Assert.Equal(
            rules.Length,
            rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count()
        );
        Assert.Equal(
            rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).Select(rule => rule.Id),
            rules.Select(rule => rule.Id)
        );
    }

    [Fact]
    public void RuleCatalog_HelpLinksResolveToPublishedDocsSitePages()
    {
        var docsRoot = Path.Combine(_repoRoot, "docs");
        var siteConfig = File.ReadAllLines(Path.Combine(docsRoot, "_config.yml"));
        var siteUrl = ReadYamlScalar(siteConfig, "url");
        var baseUrl = ReadYamlScalar(siteConfig, "baseurl");
        var excluded = siteConfig
            .SkipWhile(line => !line.StartsWith("exclude:", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  - ", StringComparison.Ordinal))
            .Select(line => line.Substring(4).Trim())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal($"{siteUrl}{baseUrl}/", RuleCatalog.DocumentationSiteUri);

        var failures = new List<string>();
        foreach (var rule in RuleCatalog.All)
        {
            if (!rule.HelpLinkUri.StartsWith(RuleCatalog.DocumentationSiteUri, StringComparison.Ordinal) ||
                !rule.HelpLinkUri.EndsWith(".html", StringComparison.Ordinal))
            {
                failures.Add($"{rule.Id}: help link {rule.HelpLinkUri} is not a docs site page.");
                continue;
            }

            // Jekyll publishes docs/<page>.md at <baseurl>/<page>.html unless the page is excluded
            // or overrides its permalink, so the source file must sit at the docs root.
            var pageName = rule.HelpLinkUri.Substring(RuleCatalog.DocumentationSiteUri.Length);
            var sourceName = Path.ChangeExtension(pageName, ".md");
            var sourcePath = Path.Combine(docsRoot, sourceName);

            if (!string.Equals(rule.DocumentationPath, "docs/" + sourceName, StringComparison.Ordinal))
                failures.Add($"{rule.Id}: help link page {pageName} does not match {rule.DocumentationPath}.");
            if (!File.Exists(sourcePath))
            {
                failures.Add($"{rule.Id}: missing docs page source {sourcePath}.");
                continue;
            }

            if (excluded.Contains(sourceName))
                failures.Add($"{rule.Id}: {sourceName} is excluded from the docs site.");

            var frontMatter = File.ReadLines(sourcePath)
                .Skip(1)
                .TakeWhile(line => line.Trim() != "---")
                .ToArray();
            if (File.ReadLines(sourcePath).FirstOrDefault()?.Trim() != "---" ||
                !frontMatter.Any(line => line.StartsWith("title:", StringComparison.Ordinal)))
            {
                failures.Add($"{rule.Id}: {sourceName} needs front matter with a title so Pages renders it and lists it in the sitemap.");
            }

            if (frontMatter.Any(line => line.StartsWith("permalink:", StringComparison.Ordinal)))
                failures.Add($"{rule.Id}: {sourceName} overrides its permalink, which would break the help link.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string ReadYamlScalar(IEnumerable<string> lines, string key)
    {
        var line = lines.Single(candidate => candidate.StartsWith(key + ":", StringComparison.Ordinal));
        return line.Substring(key.Length + 1).Trim().Trim('"');
    }

    [Fact]
    public void RuleCatalog_EntriesMatchRepositoryLayout()
    {
        var failures = new List<string>();

        foreach (var rule in RuleCatalog.All)
        {
            var analyzerDir = Path.Combine(
                _repoRoot,
                rule.AnalyzerSourcePath.Replace('/', Path.DirectorySeparatorChar)
            );
            var testDir = Path.Combine(
                _repoRoot,
                "tests",
                "LinqContraband.Tests",
                "Analyzers",
                rule.Slug
            );
            var docPath = Path.Combine(
                _repoRoot,
                rule.DocumentationPath.Replace('/', Path.DirectorySeparatorChar)
            );
            var sampleDir = Path.Combine(
                _repoRoot,
                "samples",
                "LinqContraband.Sample",
                "Samples",
                rule.Slug
            );
            var samplePath = Path.Combine(
                _repoRoot,
                rule.SamplePath.Replace('/', Path.DirectorySeparatorChar)
            );

            if (!Directory.Exists(analyzerDir))
                failures.Add($"{rule.Id}: missing analyzer directory {analyzerDir}");

            var analyzerFiles = Directory.Exists(analyzerDir)
                ? Directory.GetFiles(analyzerDir, "*Analyzer.cs", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (analyzerFiles.Length != 1)
                failures.Add($"{rule.Id}: expected exactly one analyzer file in {analyzerDir}");
            else if (
                !Path.GetFileNameWithoutExtension(analyzerFiles[0])
                    .Equals(rule.AnalyzerTypeName, StringComparison.Ordinal)
            )
                failures.Add(
                    $"{rule.Id}: analyzer type mismatch. Catalog={rule.AnalyzerTypeName}, file={Path.GetFileNameWithoutExtension(analyzerFiles[0])}"
                );

            if (!Directory.Exists(testDir))
            {
                failures.Add($"{rule.Id}: missing test directory {testDir}");
            }
            else if (Directory.GetFiles(testDir, "*.cs", SearchOption.TopDirectoryOnly).Length == 0)
            {
                failures.Add($"{rule.Id}: test directory exists but contains no C# test files");
            }

            if (!Directory.Exists(sampleDir))
                failures.Add($"{rule.Id}: missing sample directory {sampleDir}");

            if (!File.Exists(samplePath))
                failures.Add($"{rule.Id}: missing sample file {samplePath}");
            else if (
                !Path.GetDirectoryName(samplePath)!.Equals(sampleDir, StringComparison.Ordinal)
            )
                failures.Add(
                    $"{rule.Id}: sample path should live under {sampleDir} but was {samplePath}"
                );

            if (!File.Exists(docPath))
                failures.Add($"{rule.Id}: missing documentation file {docPath}");

            var fixerFiles = Directory.Exists(analyzerDir)
                ? Directory.GetFiles(analyzerDir, "*Fixer.cs", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            if (rule.HasCodeFix)
            {
                if (fixerFiles.Length != 1)
                    failures.Add(
                        $"{rule.Id}: catalog says a fixer exists but repository layout does not contain exactly one fixer file"
                    );
                else if (
                    rule.FixerTypeName == null
                    || !Path.GetFileNameWithoutExtension(fixerFiles[0])
                        .Equals(rule.FixerTypeName, StringComparison.Ordinal)
                )
                    failures.Add(
                        $"{rule.Id}: fixer type mismatch. Catalog={rule.FixerTypeName ?? "<null>"}, file={Path.GetFileNameWithoutExtension(fixerFiles[0])}"
                    );

                if (!string.IsNullOrWhiteSpace(rule.NoCodeFixRationale))
                    failures.Add(
                        $"{rule.Id}: fixer-enabled rule should not declare a no-code-fix rationale"
                    );
            }
            else
            {
                if (fixerFiles.Length != 0)
                    failures.Add(
                        $"{rule.Id}: catalog says no fixer exists but repository layout contains fixer files"
                    );

                if (string.IsNullOrWhiteSpace(rule.NoCodeFixRationale))
                    failures.Add($"{rule.Id}: non-fixable rule must declare a rationale");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RepositoryCounts_MatchTheCatalog()
    {
        var analyzerDirectories = Directory
            .GetFiles(
                Path.Combine(_repoRoot, "src", "LinqContraband", "Analyzers"),
                "*Analyzer.cs",
                SearchOption.AllDirectories
            )
            .Select(Path.GetDirectoryName)
            .Where(path => path is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var testDirectories = Directory.GetDirectories(
            Path.Combine(_repoRoot, "tests", "LinqContraband.Tests", "Analyzers")
        );
        var sampleDirectories = Directory.GetDirectories(
            Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", "Samples")
        );
        var documentationFiles = Directory.GetFiles(
            Path.Combine(_repoRoot, "docs"),
            "LC*.md",
            SearchOption.TopDirectoryOnly
        );

        Assert.Equal(RuleCatalog.All.Length, analyzerDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, testDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, sampleDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, documentationFiles.Length);
    }
}
