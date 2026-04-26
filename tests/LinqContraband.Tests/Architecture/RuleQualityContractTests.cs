using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace LinqContraband.Tests.Architecture;

public sealed class RuleQualityContractTests
{
    private const string RepositoryUrl = "https://github.com/georgepwall1991/LinqContraband";
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void PackageMetadata_PointsToThePublicRepository()
    {
        var properties = LoadPackageProperties();

        Assert.Equal("LinqContraband", properties["PackageId"]);
        Assert.Equal("LinqContraband", properties["Title"]);
        Assert.Equal(RepositoryUrl, properties["RepositoryUrl"]);
        Assert.Equal(RepositoryUrl, properties["PackageProjectUrl"]);
        Assert.True(properties.TryGetValue("PackageReleaseNotes", out var releaseNotes) && !string.IsNullOrWhiteSpace(releaseNotes));
    }

    [Fact]
    public void PackageReleaseNotes_MatchCurrentChangelogEntry()
    {
        var properties = LoadPackageProperties();
        var version = properties["Version"];
        var releaseNotes = properties["PackageReleaseNotes"];
        var changelog = File.ReadAllText(Path.Combine(_repoRoot, "CHANGELOG.md"));
        var currentEntry = ExtractChangelogEntry(changelog, version);

        Assert.False(string.IsNullOrWhiteSpace(currentEntry), $"CHANGELOG.md should contain a ## [{version}] entry.");

        var changelogRuleIds = ExtractRuleIds(currentEntry).ToArray();
        var releaseNoteRuleIds = ExtractRuleIds(releaseNotes).ToArray();

        if (changelogRuleIds.Length == 0)
            return;

        var missingRuleIds = changelogRuleIds.Except(releaseNoteRuleIds, StringComparer.Ordinal).ToArray();
        var staleRuleIds = releaseNoteRuleIds.Except(changelogRuleIds, StringComparer.Ordinal).ToArray();

        Assert.True(
            missingRuleIds.Length == 0 && staleRuleIds.Length == 0,
            $"PackageReleaseNotes should reference the same LC rule ids as CHANGELOG.md {version}. Missing: {FormatIds(missingRuleIds)}; stale: {FormatIds(staleRuleIds)}.");
    }

    [Fact]
    public void EditorConfig_ListsEveryCatalogRuleSeverity()
    {
        var editorConfig = File.ReadAllText(Path.Combine(_repoRoot, ".editorconfig"));
        var failures = RuleCatalog.All
            .Where(rule => !editorConfig.Contains($"dotnet_diagnostic.{rule.Id}.severity", StringComparison.Ordinal))
            .Select(rule => $"{rule.Id}: .editorconfig is missing a severity entry.")
            .ToArray();

        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void FixerRules_ExportCodeFixProvidersForTheirRuleIds()
    {
        var analyzerAssembly = typeof(LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer).Assembly;
        var failures = new List<string>();

        foreach (var rule in RuleCatalog.All.Where(rule => rule.HasCodeFix))
        {
            var fixerType = analyzerAssembly.GetTypes().SingleOrDefault(type => type.Name == rule.FixerTypeName);
            if (fixerType is null)
            {
                failures.Add($"{rule.Id}: could not find fixer type '{rule.FixerTypeName}'.");
                continue;
            }

            if (!typeof(CodeFixProvider).IsAssignableFrom(fixerType))
            {
                failures.Add($"{rule.Id}: fixer type '{fixerType.Name}' does not inherit from CodeFixProvider.");
                continue;
            }

            var export = fixerType.GetCustomAttributes(typeof(ExportCodeFixProviderAttribute), inherit: false)
                .Cast<ExportCodeFixProviderAttribute>()
                .SingleOrDefault();
            if (export is null)
            {
                failures.Add($"{rule.Id}: fixer type '{fixerType.Name}' is missing ExportCodeFixProviderAttribute.");
            }
            else if (!export.Languages.Contains(LanguageNames.CSharp, StringComparer.Ordinal))
            {
                failures.Add($"{rule.Id}: fixer type '{fixerType.Name}' is not exported for C#.");
            }

            var fixer = (CodeFixProvider)Activator.CreateInstance(fixerType)!;
            if (!fixer.FixableDiagnosticIds.Contains(rule.Id, StringComparer.Ordinal))
            {
                failures.Add($"{rule.Id}: fixer type '{fixerType.Name}' does not list the rule id in FixableDiagnosticIds.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void RuleDocs_DeclareShippedBehaviorAndFixStrategy()
    {
        var failures = new List<string>();

        foreach (var rule in RuleCatalog.All)
        {
            var docPath = Path.Combine(_repoRoot, rule.DocumentationPath.Replace('/', Path.DirectorySeparatorChar));
            var markdown = File.ReadAllText(docPath);

            if (!markdown.Contains(rule.Id, StringComparison.Ordinal))
                failures.Add($"{rule.Id}: documentation should mention the rule id.");

            if (markdown.Contains("## Implementation Plan", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{rule.Id}: documentation should describe shipped behavior, not an implementation plan.");

            if (rule.HasCodeFix && ContainsAny(markdown, "no automatic fix", "no code fix", "manual-only", "manual only"))
                failures.Add($"{rule.Id}: fixer-enabled documentation should not describe the rule as manual-only.");

            if (!rule.HasCodeFix && ContainsAny(markdown, "## Code Fix", "## Fixer Behavior"))
                failures.Add($"{rule.Id}: manual-only documentation should not advertise fixer behavior.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void SampleDiagnosticManifest_MatchesCatalogAndFiles()
    {
        var manifestPath = Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", "sample-diagnostics.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expectations = document.RootElement.GetProperty("expectations").EnumerateArray().ToArray();
        var safeSamples = document.RootElement.TryGetProperty("safeSamples", out var safeSamplesElement)
            ? safeSamplesElement.EnumerateArray().Select(value => value.GetString()).ToArray()
            : Array.Empty<string?>();
        var knownRuleIds = RuleCatalog.All.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        var catalogSamplePaths = RuleCatalog.All
            .Select(rule => rule.SamplePath.Replace("samples/LinqContraband.Sample/", string.Empty, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var manifestSamplePaths = new HashSet<string>(StringComparer.Ordinal);
        var manifestDiagnosticPaths = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();

        if (safeSamples.Length == 0)
            failures.Add("Manifest should declare at least one safeSamples path for false-positive regression coverage.");

        foreach (var expectation in expectations)
        {
            var diagnosticPath = expectation.GetProperty("diagnosticPath").GetString();
            if (string.IsNullOrWhiteSpace(diagnosticPath))
            {
                failures.Add("Manifest contains an expectation with no diagnosticPath.");
                continue;
            }

            manifestDiagnosticPaths.Add(diagnosticPath);
            var diagnosticFile = Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", diagnosticPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(diagnosticFile))
                failures.Add($"Manifest diagnosticPath does not exist: {diagnosticPath}");

            var samplePaths = expectation.GetProperty("samplePaths").EnumerateArray().Select(value => value.GetString()).ToArray();
            foreach (var samplePath in samplePaths)
            {
                if (string.IsNullOrWhiteSpace(samplePath))
                {
                    failures.Add($"{diagnosticPath}: samplePaths contains a blank value.");
                    continue;
                }

                manifestSamplePaths.Add(samplePath);
                var sampleFile = Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", samplePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sampleFile))
                    failures.Add($"Manifest samplePath does not exist: {samplePath}");
            }

            var ruleIds = expectation.GetProperty("ruleIds").EnumerateArray().Select(value => value.GetString()).ToArray();
            foreach (var ruleId in ruleIds)
            {
                if (string.IsNullOrWhiteSpace(ruleId) || !knownRuleIds.Contains(ruleId))
                    failures.Add($"{diagnosticPath}: unknown expected rule id '{ruleId}'.");
            }
        }

        foreach (var safeSample in safeSamples)
        {
            if (string.IsNullOrWhiteSpace(safeSample))
            {
                failures.Add("Manifest safeSamples contains a blank value.");
                continue;
            }

            var safeSampleFile = Path.Combine(_repoRoot, "samples", "LinqContraband.Sample", safeSample.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(safeSampleFile))
                failures.Add($"Manifest safeSample path does not exist: {safeSample}");

            if (manifestDiagnosticPaths.Contains(safeSample) || manifestSamplePaths.Contains(safeSample))
                failures.Add($"Manifest safeSample path is also listed as a diagnostic sample: {safeSample}");
        }

        foreach (var missingSamplePath in catalogSamplePaths.Except(manifestSamplePaths, StringComparer.Ordinal))
            failures.Add($"Manifest is missing catalog sample path: {missingSamplePath}");

        foreach (var extraSamplePath in manifestSamplePaths.Except(catalogSamplePaths, StringComparer.Ordinal))
            failures.Add($"Manifest contains non-catalog sample path: {extraSamplePath}");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, string> LoadPackageProperties()
    {
        var projectPath = Path.Combine(_repoRoot, "src", "LinqContraband", "LinqContraband.csproj");
        var document = XDocument.Load(projectPath);
        return document.Descendants("PropertyGroup").Elements()
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);
    }

    private static string ExtractChangelogEntry(string changelog, string version)
    {
        var heading = $"## [{version}]";
        var start = changelog.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        var next = changelog.IndexOf("\n## [", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? changelog[start..] : changelog[start..next];
    }

    private static IEnumerable<string> ExtractRuleIds(string value)
    {
        return Regex.Matches(value, @"LC\d{3}")
            .Cast<Match>()
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);
    }

    private static string FormatIds(string[] ruleIds)
    {
        return ruleIds.Length == 0 ? "<none>" : string.Join(", ", ruleIds);
    }
}
