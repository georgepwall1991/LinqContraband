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

        Assert.Equal(43, rules.Length);
        Assert.Equal(rules.Length, rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).Select(rule => rule.Id), rules.Select(rule => rule.Id));
    }

    [Fact]
    public void RuleCatalog_EntriesMatchRepositoryLayout()
    {
        var failures = new List<string>();

        foreach (var rule in RuleCatalog.All)
        {
            var analyzerDir = Path.Combine(_repoRoot, "src", "LinqContraband", "Analyzers", rule.Slug);
            var testDir = Path.Combine(_repoRoot, "tests", "LinqContraband.Tests", "Analyzers", rule.Slug);
            var docPath = Path.Combine(_repoRoot, rule.DocumentationPath.Replace('/', Path.DirectorySeparatorChar));
            var sampleDir = Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", "Samples", rule.Slug);
            var samplePath = Path.Combine(_repoRoot, rule.SamplePath.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(analyzerDir))
                failures.Add($"{rule.Id}: missing analyzer directory {analyzerDir}");

            var analyzerFiles = Directory.Exists(analyzerDir)
                ? Directory.GetFiles(analyzerDir, "*Analyzer.cs", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (analyzerFiles.Length != 1)
                failures.Add($"{rule.Id}: expected exactly one analyzer file in {analyzerDir}");
            else if (!Path.GetFileNameWithoutExtension(analyzerFiles[0]).Equals(rule.AnalyzerTypeName, StringComparison.Ordinal))
                failures.Add($"{rule.Id}: analyzer type mismatch. Catalog={rule.AnalyzerTypeName}, file={Path.GetFileNameWithoutExtension(analyzerFiles[0])}");

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
            else if (!Path.GetDirectoryName(samplePath)!.Equals(sampleDir, StringComparison.Ordinal))
                failures.Add($"{rule.Id}: sample path should live under {sampleDir} but was {samplePath}");

            if (!File.Exists(docPath))
                failures.Add($"{rule.Id}: missing documentation file {docPath}");

            var fixerFiles = Directory.Exists(analyzerDir)
                ? Directory.GetFiles(analyzerDir, "*Fixer.cs", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            if (rule.HasCodeFix)
            {
                if (fixerFiles.Length != 1)
                    failures.Add($"{rule.Id}: catalog says a fixer exists but repository layout does not contain exactly one fixer file");
                else if (rule.FixerTypeName == null || !Path.GetFileNameWithoutExtension(fixerFiles[0]).Equals(rule.FixerTypeName, StringComparison.Ordinal))
                    failures.Add($"{rule.Id}: fixer type mismatch. Catalog={rule.FixerTypeName ?? "<null>"}, file={Path.GetFileNameWithoutExtension(fixerFiles[0])}");

                if (!string.IsNullOrWhiteSpace(rule.NoCodeFixRationale))
                    failures.Add($"{rule.Id}: fixer-enabled rule should not declare a no-code-fix rationale");
            }
            else
            {
                if (fixerFiles.Length != 0)
                    failures.Add($"{rule.Id}: catalog says no fixer exists but repository layout contains fixer files");

                if (string.IsNullOrWhiteSpace(rule.NoCodeFixRationale))
                    failures.Add($"{rule.Id}: non-fixable rule must declare a rationale");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RepositoryCounts_MatchTheCatalog()
    {
        var analyzerDirectories = Directory.GetDirectories(Path.Combine(_repoRoot, "src", "LinqContraband", "Analyzers"));
        var testDirectories = Directory.GetDirectories(Path.Combine(_repoRoot, "tests", "LinqContraband.Tests", "Analyzers"));
        var sampleDirectories = Directory.GetDirectories(Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", "Samples"));
        var documentationFiles = Directory.GetFiles(Path.Combine(_repoRoot, "docs"), "LC*.md", SearchOption.TopDirectoryOnly);

        Assert.Equal(RuleCatalog.All.Length, analyzerDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, testDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, sampleDirectories.Length);
        Assert.Equal(RuleCatalog.All.Length, documentationFiles.Length);
    }
}
