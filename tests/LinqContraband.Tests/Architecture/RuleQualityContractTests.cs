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
    private const string AuditedCommitMetadataPattern = @"^- Base audited commit: (?<commit>[0-9a-f]{40})\r?$";
    private const string BaselineAuditedCommitMetadataPattern = @"^Base audited commit:[^`\r\n]*`(?<commit>[0-9a-f]{40})`[^\r\n]*\r?$";
    private const string ReleasePackageVersionMetadataPattern = @"^- Package version:[ \t]*(?<version>[^\r\n]+?)\r?$";
    private const string BaselinePackageVersionMetadataPattern = @"^Package version:[ \t]*\*\*(?<version>[^*\r\n]+)\*\*[^\r\n]*\r?$";
    private const string RepositoryUrl = "https://github.com/georgepwall1991/LinqContraband";
    private const string ProjectUrl = "https://georgepwall1991.github.io/LinqContraband/";
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    [Fact]
    public void PackageMetadata_PointsToOfficialProjectSurfaces()
    {
        var properties = LoadPackageProperties();

        Assert.Equal("LinqContraband", properties["PackageId"]);
        Assert.Equal("LinqContraband", properties["Title"]);
        Assert.Equal(RepositoryUrl, properties["RepositoryUrl"]);
        Assert.Equal(ProjectUrl, properties["PackageProjectUrl"]);
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
    public void PublicDocumentation_MatchesCatalogAndPackageSurfaces()
    {
        var properties = LoadPackageProperties();
        var version = properties["Version"];
        var readme = File.ReadAllText(Path.Combine(_repoRoot, "README.md"));
        var health = File.ReadAllText(Path.Combine(_repoRoot, "docs", "analyzer-health.md"));
        var changelog = File.ReadAllText(Path.Combine(_repoRoot, "CHANGELOG.md"));
        var currentEntry = ExtractChangelogEntry(changelog, version);
        var failures = new List<string>();

        if (!readme.Contains($"**{RuleCatalog.All.Length} rules**", StringComparison.Ordinal))
            failures.Add($"README.md should declare the current {RuleCatalog.All.Length}-rule catalog size.");

        foreach (var rule in RuleCatalog.All)
        {
            if (!readme.Contains($"### {rule.Id}:", StringComparison.Ordinal))
                failures.Add($"README.md should contain a section for {rule.Id}.");
        }

        var relativeDestinations = ExtractPotentialMarkdownDestinations(readme)
            .Where(destination => !destination.StartsWith('#') &&
                                  !Uri.TryCreate(destination, UriKind.Absolute, out _))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(destination => destination, StringComparer.Ordinal)
            .ToArray();
        if (relativeDestinations.Length > 0)
            failures.Add($"README.md should avoid relative link-like destinations anywhere in its packaged source. Relative destinations: {string.Join(", ", relativeDestinations)}.");

        var releaseMetadata = ExtractMarkdownSection(health, "Release metadata:");
        if (releaseMetadata is null ||
            !string.Equals(ExtractReleasePackageVersion(releaseMetadata), version, StringComparison.Ordinal))
        {
            failures.Add($"The release metadata should name package version {version}.");
        }
        var releaseAuditedCommit = releaseMetadata is null
            ? null
            : ExtractReleaseAuditedCommit(releaseMetadata);

        const string baselineMarker = "## Verification Baseline";
        var baseline = ExtractMarkdownSection(health, baselineMarker);
        if (baseline is null)
        {
            failures.Add("docs/analyzer-health.md should contain a Verification Baseline section.");
        }
        else
        {
            if (!string.Equals(ExtractBaselinePackageVersion(baseline), version, StringComparison.Ordinal))
                failures.Add($"The Verification Baseline should name package version {version}.");

            var baselineAuditedCommit = ExtractBaselineAuditedCommit(baseline);
            if (releaseAuditedCommit is null ||
                !string.Equals(baselineAuditedCommit, releaseAuditedCommit, StringComparison.Ordinal))
            {
                failures.Add("The Verification Baseline should repeat the current release metadata commit.");
            }

            var currentVerification = baseline
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("Current verification (", StringComparison.Ordinal));
            foreach (var ruleId in ExtractRuleIds(currentEntry))
            {
                if (currentVerification is null || !currentVerification.Contains(ruleId, StringComparison.Ordinal))
                    failures.Add($"The current Verification Baseline heading should identify {ruleId}.");
            }

            var scorecardStart = health.IndexOf("## Scorecard", StringComparison.Ordinal);
            var scorecardTableStart = health.IndexOf("| Rule |", scorecardStart, StringComparison.Ordinal);
            var scorecardIntro = scorecardStart >= 0 && scorecardTableStart > scorecardStart
                ? health.Substring(scorecardStart, scorecardTableStart - scorecardStart)
                : string.Empty;
            var scorecardSuiteTotal = Regex.Match(
                scorecardIntro,
                @"suite to \*\*(?<count>[\d,]+) tests\*\*",
                RegexOptions.CultureInvariant);
            var currentSuiteTotal = Regex.Matches(
                    baseline,
                    @"full local net10\.0 suite passes (?<count>[\d,]+) tests",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .LastOrDefault();
            if (!scorecardSuiteTotal.Success ||
                currentSuiteTotal is null ||
                !string.Equals(
                    scorecardSuiteTotal.Groups["count"].Value,
                    currentSuiteTotal.Groups["count"].Value,
                    StringComparison.Ordinal))
            {
                failures.Add("The scorecard introduction and latest Verification Baseline should declare the same full-suite test total.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ReleaseMetadataPatterns_AcceptCrLfAndBindExactFields()
    {
        const string metadata = "- Base audited commit: d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544\r\n";

        var match = Regex.Match(
            metadata,
            AuditedCommitMetadataPattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.True(match.Success);
        const string baseline = """
            ## Verification Baseline

            Base audited commit: master at `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa` (stale state).

            Historical verification mentioned `d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544`.
            """;

        Assert.Equal(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ExtractBaselineAuditedCommit(baseline));

        const string sameLineHistoricalMention = """
            ## Verification Baseline

            Base audited commit: master at `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa` (historical comparison: `d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544`).
            """;
        Assert.Equal(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ExtractBaselineAuditedCommit(sameLineHistoricalMention));

        const string laterSectionOnly = """
            ## Verification Baseline

            Package version: **5.7.0**

            ## Historical Appendix

            Base audited commit: master at `d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544`.
            """;
        var boundedBaseline = ExtractMarkdownSection(laterSectionOnly, "## Verification Baseline");
        Assert.NotNull(boundedBaseline);
        Assert.Null(ExtractBaselineAuditedCommit(boundedBaseline!));

        const string archiveOnly = """
            ## Verification Baseline Archive

            Package version: **5.7.0**
            Base audited commit: master at `d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544`.
            """;
        Assert.Null(ExtractMarkdownSection(archiveOnly, "## Verification Baseline"));

        const string releaseVersionBypass = """
            Release metadata:

            - Package version: 5.6.0
            Historical comparison: - Package version: 5.7.0

            ## Rubric
            """;
        var releaseMetadata = ExtractMarkdownSection(releaseVersionBypass, "Release metadata:");
        Assert.NotNull(releaseMetadata);
        Assert.Equal("5.6.0", ExtractReleasePackageVersion(releaseMetadata!));

        const string releaseCommitBypass = """
            Release metadata:

            - Package version: 5.7.0

            ## Historical Appendix

            - Base audited commit: d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544
            """;
        var releaseMetadataWithoutCommit = ExtractMarkdownSection(releaseCommitBypass, "Release metadata:");
        Assert.NotNull(releaseMetadataWithoutCommit);
        Assert.Null(ExtractReleaseAuditedCommit(releaseMetadataWithoutCommit!));

        const string baselineVersionBypass = """
            ## Verification Baseline

            Package version: **5.6.0**
            Historical comparison: Package version: **5.7.0**
            """;
        Assert.Equal("5.6.0", ExtractBaselinePackageVersion(baselineVersionBypass));
    }

    [Fact]
    public void PotentialMarkdownDestinationExtraction_FindsEveryLinkLikeSourceForm()
    {
        const string markdown = """
            [![Badge](https://example.test/image.svg)](relative-inline.md)
            [Spaced](   spaced-inline.md)
            [Line break](
              line-break-inline.md)
            [Catalog][docs]

            [docs]:
            docs/rule-catalog.md
                [indented]: docs/indented-reference.md
            > [quoted]: docs/quoted-reference.md
            <!-- [commented]: docs/commented-reference.md -->
            [Multiline
              label]: docs/multiline-label-reference.md
            [
            multiline
            label]: docs/unindented-multiline-reference.md
            [
            standalone close
            ]: docs/standalone-close-reference.md
            > [
            > quoted standalone close
            > ]: docs/quoted-standalone-close-reference.md
            > [Quoted
            > multiline]: docs/quoted-multiline-reference.md
            [escaped\]]: docs/escaped-close-reference.md
            <img src="relative-icon.png" alt="Icon">
            <a href=
              'relative-guide.md'>Guide</a>
            <img SRC=relative-unquoted.png alt="Unquoted">
            Document href="prose-only.md" as an example.
            \[literal](escaped-source-example.md)
            `[code](inline-code-source-example.md)`
            <!-- <a href="commented-source-example.md">Commented out</a> -->
            [outer [inner](https://example.test/inner)](nested-link-source-example.md)
            """;

        var destinations = ExtractPotentialMarkdownDestinations(markdown).ToArray();

        Assert.Contains("relative-inline.md", destinations);
        Assert.Contains("spaced-inline.md", destinations);
        Assert.Contains("line-break-inline.md", destinations);
        Assert.Contains("docs/rule-catalog.md", destinations);
        Assert.Contains("docs/indented-reference.md", destinations);
        Assert.Contains("docs/quoted-reference.md", destinations);
        Assert.Contains("docs/commented-reference.md", destinations);
        Assert.Contains("docs/multiline-label-reference.md", destinations);
        Assert.Contains("docs/unindented-multiline-reference.md", destinations);
        Assert.Contains("docs/standalone-close-reference.md", destinations);
        Assert.Contains("docs/quoted-standalone-close-reference.md", destinations);
        Assert.Contains("docs/quoted-multiline-reference.md", destinations);
        Assert.Contains("docs/escaped-close-reference.md", destinations);
        Assert.Contains("relative-icon.png", destinations);
        Assert.Contains("relative-guide.md", destinations);
        Assert.Contains("relative-unquoted.png", destinations);
        Assert.Contains("prose-only.md", destinations);
        Assert.Contains("escaped-source-example.md", destinations);
        Assert.Contains("inline-code-source-example.md", destinations);
        Assert.Contains("commented-source-example.md", destinations);
        Assert.Contains("nested-link-source-example.md", destinations);

        const string absoluteContinuations = """
            > [Guide](
            >   https://example.test/guide)

            > [docs]:
            >   https://example.test/docs
            """;
        var relativeContinuationDestinations = ExtractPotentialMarkdownDestinations(absoluteContinuations)
            .Where(destination => !Uri.TryCreate(destination, UriKind.Absolute, out _))
            .ToArray();
        Assert.Empty(relativeContinuationDestinations);
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

    private static IEnumerable<string> ExtractPotentialMarkdownDestinations(string markdown)
    {
        var inlineDestinations = Regex.Matches(
                markdown,
                @"\]\([ \t]*(?:(?:\r\n|\r|\n)[ \t]*(?:>[ \t]*)*)?(?<destination><[^>]+>|[^\s\)]+)",
                RegexOptions.CultureInvariant)
            .Cast<Match>();
        var referenceDestinations = Regex.Matches(
                markdown,
                @"\[(?:\\[^\r\n]|[^\]\\\r\n]|(?:\r\n|\r|\n)[ \t]*(?:>[ \t]*)*(?![ \t]*(?:\r\n|\r|\n)))*\]:[ \t]*(?:(?:\r\n|\r|\n)[ \t]*(?:>[ \t]*)*)?(?<destination><[^>\r\n]+>|[^\s]+)",
                RegexOptions.CultureInvariant)
            .Cast<Match>();
        var attributeDestinations = Regex.Matches(
                markdown,
                @"\b(?:href|src)\s*=\s*(?:""(?<destination>[^""]+)""|'(?<destination>[^']+)'|(?<destination>[^\s>]+))",
                RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Cast<Match>();

        return inlineDestinations
            .Concat(referenceDestinations)
            .Concat(attributeDestinations)
            .Select(match => match.Groups["destination"].Value.Trim('<', '>'));
    }

    private static string? ExtractBaselineAuditedCommit(string baseline)
    {
        var match = Regex.Match(
            baseline,
            BaselineAuditedCommitMetadataPattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["commit"].Value : null;
    }

    private static string? ExtractReleaseAuditedCommit(string releaseMetadata)
    {
        return ExtractMetadataValue(releaseMetadata, AuditedCommitMetadataPattern, "commit");
    }

    private static string? ExtractReleasePackageVersion(string releaseMetadata)
    {
        return ExtractMetadataValue(releaseMetadata, ReleasePackageVersionMetadataPattern, "version");
    }

    private static string? ExtractBaselinePackageVersion(string baseline)
    {
        return ExtractMetadataValue(baseline, BaselinePackageVersionMetadataPattern, "version");
    }

    private static string? ExtractMetadataValue(string section, string pattern, string groupName)
    {
        var match = Regex.Match(
            section,
            pattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[groupName].Value : null;
    }

    private static string? ExtractMarkdownSection(string markdown, string heading)
    {
        var headingMatch = Regex.Match(
            markdown,
            $"^{Regex.Escape(heading)}\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (!headingMatch.Success)
            return null;

        var start = headingMatch.Index;
        var next = markdown.IndexOf("\n## ", start + headingMatch.Length, StringComparison.Ordinal);
        return next < 0 ? markdown.Substring(start) : markdown.Substring(start, next - start);
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
