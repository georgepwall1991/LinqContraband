using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        var relativeDestinations = ExtractMarkdownDestinations(readme)
            .Where(destination => !destination.StartsWith("#", StringComparison.Ordinal) &&
                                  !Uri.TryCreate(destination, UriKind.Absolute, out _))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(destination => destination, StringComparer.Ordinal)
            .ToArray();
        if (relativeDestinations.Length > 0)
            failures.Add($"README.md should use package-portable absolute links. Relative destinations: {string.Join(", ", relativeDestinations)}.");

        const string baselineMarker = "## Verification Baseline";
        var baselineStart = health.IndexOf(baselineMarker, StringComparison.Ordinal);
        if (baselineStart < 0)
        {
            failures.Add("docs/analyzer-health.md should contain a Verification Baseline section.");
        }
        else
        {
            var baseline = health.Substring(baselineStart);
            if (!baseline.Contains($"Package version: **{version}**", StringComparison.Ordinal))
                failures.Add($"The Verification Baseline should name package version {version}.");

            var auditedCommitMatch = Regex.Match(
                health,
                AuditedCommitMetadataPattern,
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (!auditedCommitMatch.Success ||
                !baseline.Contains($"`{auditedCommitMatch.Groups["commit"].Value}`", StringComparison.Ordinal))
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
    public void AuditedCommitMetadataPattern_AcceptsCrLf()
    {
        const string metadata = "- Base audited commit: d375b9ea7f9d5d39d385abf6a8e4ad1e1db10544\r\n";

        var match = Regex.Match(
            metadata,
            AuditedCommitMetadataPattern,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.True(match.Success);
    }

    [Fact]
    public void MarkdownDestinationExtraction_FindsNestedAndReferenceLinks()
    {
        const string markdown = """
            [![Badge](https://example.test/image.svg)](relative-inline.md)
            [Catalog][docs]

            [docs]:
            docs/rule-catalog.md
            <img src="relative-icon.png" alt="Icon">
            <a href=
              'relative-guide.md'>Guide</a>
            <img SRC=relative-unquoted.png alt="Unquoted">
            Paragraph before a continued raw HTML tag.
                <a href="paragraph-continuation.md">Paragraph continuation</a>
            > ~~~html
            <a href="visible-outside-quote.md">Visible outside quote</a>
            > ~~~
            - Item
                <a href="visible-in-item.md">Visible in item</a>
            """;
        const string invalidMarkdown = """
            [blank]:

            docs/rule-catalog.md
            [prose]:
            This is unrelated prose.
            Document href="prose-only.md" as an example.
            <!-- <a href="commented-out.md">Commented out</a> -->
            `<a href="inline-code.md">Inline example</a>`
            ~~~html
            <img src="tilde-fence.png" alt="Fence example">
            ~~~
                <a href="indented-code.md">Indented example</a>
            [Guide]`example`(synthetic-link.md)
            > ~~~html
            > <a href="blockquote-fence.md">Quoted fence</a>
            > ~~~
            # Heading
                <a href="heading-code.md">Heading code example</a>
            ```html
            > ```
            <a href="top-level-fence.md">Still fenced</a>
            ```
            - ```html
              <a href="list-fence.md">List fence</a>
              ```
            """;

        var destinations = ExtractMarkdownDestinations(markdown).ToArray();
        var invalidDestinations = ExtractMarkdownDestinations(invalidMarkdown).ToArray();

        Assert.Contains("relative-inline.md", destinations);
        Assert.Contains("docs/rule-catalog.md", destinations);
        Assert.Contains("relative-icon.png", destinations);
        Assert.Contains("relative-guide.md", destinations);
        Assert.Contains("relative-unquoted.png", destinations);
        Assert.Contains("paragraph-continuation.md", destinations);
        Assert.Contains("visible-outside-quote.md", destinations);
        Assert.Contains("visible-in-item.md", destinations);
        Assert.Empty(invalidDestinations);
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

    private static IEnumerable<string> ExtractMarkdownDestinations(string markdown)
    {
        var withoutCode = StripMarkdownCode(markdown);

        var inlineDestinations = Regex.Matches(
                withoutCode,
                @"\]\((?<destination><[^>]+>|[^\s\)]+)",
                RegexOptions.CultureInvariant)
            .Cast<Match>();
        var referenceDestinations = Regex.Matches(
                withoutCode,
                @"^[ \t]{0,3}\[[^\]\r\n]+\]:(?:[ \t]*(?:\r\n|\r|\n)[ \t]*|[ \t]*)(?<destination><[^>\r\n]+>|[^\s]+)(?:[ \t]+(?:""[^""\r\n]*""|'[^'\r\n]*'|\([^\)\r\n]*\)))?[ \t]*\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Cast<Match>();
        return inlineDestinations
            .Concat(referenceDestinations)
            .Select(match => match.Groups["destination"].Value.Trim('<', '>'))
            .Concat(ExtractHtmlDestinations(withoutCode));
    }

    private static string StripMarkdownCode(string markdown)
    {
        var visibleLines = new StringBuilder(markdown.Length);
        using var reader = new StringReader(markdown);
        char fenceMarker = '\0';
        var fenceLength = 0;
        var fenceQuoteDepth = 0;
        var fenceListIndent = 0;
        var canStartIndentedCode = true;
        var inIndentedCode = false;

        while (reader.ReadLine() is { } line)
        {
            var fenceLine = StripBlockQuotePrefixes(line, out var quoteDepth);
            if (fenceMarker != '\0')
            {
                if (quoteDepth < fenceQuoteDepth)
                {
                    fenceMarker = '\0';
                    fenceLength = 0;
                    fenceQuoteDepth = 0;
                    fenceListIndent = 0;
                    canStartIndentedCode = true;
                }
                else
                {
                    var closingFenceLine = StripBlockQuotePrefixes(line, out _, fenceQuoteDepth);
                    if (fenceListIndent > 0 &&
                        !TryStripIndent(closingFenceLine, fenceListIndent, out closingFenceLine))
                    {
                        if (string.IsNullOrWhiteSpace(closingFenceLine))
                            continue;

                        fenceMarker = '\0';
                        fenceLength = 0;
                        fenceQuoteDepth = 0;
                        fenceListIndent = 0;
                        canStartIndentedCode = true;
                    }
                    else
                    {
                        if (IsClosingFence(closingFenceLine, fenceMarker, fenceLength))
                        {
                            fenceMarker = '\0';
                            fenceLength = 0;
                            fenceQuoteDepth = 0;
                            fenceListIndent = 0;
                            canStartIndentedCode = true;
                        }

                        continue;
                    }
                }
            }

            var openingFenceLine = fenceLine;
            var openingListIndent = 0;
            if (TryStripListMarker(fenceLine, out var listContent, out var listIndent))
            {
                openingFenceLine = listContent;
                openingListIndent = listIndent;
            }

            if (TryGetOpeningFence(openingFenceLine, out fenceMarker, out fenceLength))
            {
                fenceQuoteDepth = quoteDepth;
                fenceListIndent = openingListIndent;
                inIndentedCode = false;
                canStartIndentedCode = true;
                continue;
            }

            var isBlank = string.IsNullOrWhiteSpace(line);
            var isIndented = line.StartsWith("    ", StringComparison.Ordinal) ||
                             line.StartsWith("\t", StringComparison.Ordinal);
            if (inIndentedCode)
            {
                if (isBlank || isIndented)
                    continue;

                inIndentedCode = false;
            }

            if (isIndented && canStartIndentedCode)
            {
                inIndentedCode = true;
                continue;
            }

            visibleLines.AppendLine(line);
            canStartIndentedCode = isBlank || IsNonParagraphBlockBoundary(line);
        }

        return StripInlineCodeSpans(visibleLines.ToString());
    }

    private static string StripBlockQuotePrefixes(string line, out int depth, int maxDepth = int.MaxValue)
    {
        var index = 0;
        depth = 0;
        while (index < line.Length && depth < maxDepth)
        {
            var cursor = index;
            var spaces = 0;
            while (cursor < line.Length && spaces < 3 && line[cursor] == ' ')
            {
                cursor++;
                spaces++;
            }

            if (cursor >= line.Length || line[cursor] != '>')
                break;

            depth++;
            index = cursor + 1;
            if (index < line.Length && line[index] is ' ' or '\t')
                index++;
        }

        return depth > 0 ? line.Substring(index) : line;
    }

    private static bool TryStripListMarker(string line, out string content, out int contentIndent)
    {
        content = line;
        contentIndent = 0;
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
            index++;

        var markerEnd = index;
        if (markerEnd < line.Length && line[markerEnd] is '*' or '+' or '-')
        {
            markerEnd++;
        }
        else
        {
            var digits = 0;
            while (markerEnd < line.Length && digits < 9 && char.IsDigit(line[markerEnd]))
            {
                markerEnd++;
                digits++;
            }

            if (digits == 0 || markerEnd >= line.Length || line[markerEnd] is not ('.' or ')'))
                return false;

            markerEnd++;
        }

        var whitespace = 0;
        while (markerEnd + whitespace < line.Length &&
               whitespace < 4 &&
               line[markerEnd + whitespace] == ' ')
        {
            whitespace++;
        }

        if (whitespace == 0)
            return false;

        contentIndent = markerEnd + whitespace;
        content = line.Substring(contentIndent);
        return true;
    }

    private static bool TryStripIndent(string line, int requiredIndent, out string content)
    {
        var index = 0;
        var indent = 0;
        while (index < line.Length && indent < requiredIndent)
        {
            if (line[index] == ' ')
            {
                indent++;
                index++;
            }
            else if (line[index] == '\t')
            {
                indent = ((indent / 4) + 1) * 4;
                index++;
            }
            else
            {
                break;
            }
        }

        content = line.Substring(index);
        return indent >= requiredIndent;
    }

    private static bool IsNonParagraphBlockBoundary(string line)
    {
        var content = StripBlockQuotePrefixes(line, out _);
        return Regex.IsMatch(
            content,
            @"^[ ]{0,3}(?:(?:#{1,6})(?:[ \t]+|$)|(?:=+|-+)[ \t]*$|(?:(?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})$)",
            RegexOptions.CultureInvariant);
    }

    private static bool TryGetOpeningFence(string line, out char marker, out int length)
    {
        marker = '\0';
        length = 0;
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
            index++;

        if (index >= line.Length || line[index] is not ('`' or '~'))
            return false;

        marker = line[index];
        while (index + length < line.Length && line[index + length] == marker)
            length++;

        if (length < 3 ||
            (marker == '`' && line.IndexOf('`', index + length) >= 0))
        {
            marker = '\0';
            length = 0;
            return false;
        }

        return true;
    }

    private static bool IsClosingFence(string line, char marker, int openingLength)
    {
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
            index++;

        var length = 0;
        while (index + length < line.Length && line[index + length] == marker)
            length++;

        if (length < openingLength)
            return false;

        return line.AsSpan(index + length).Trim().IsEmpty;
    }

    private static string StripInlineCodeSpans(string markdown)
    {
        var visible = new StringBuilder(markdown.Length);
        for (var index = 0; index < markdown.Length;)
        {
            if (markdown[index] != '`')
            {
                visible.Append(markdown[index++]);
                continue;
            }

            var openingLength = 1;
            while (index + openingLength < markdown.Length && markdown[index + openingLength] == '`')
                openingLength++;

            var cursor = index + openingLength;
            var closingEnd = -1;
            while (cursor < markdown.Length)
            {
                if (markdown[cursor] != '`')
                {
                    cursor++;
                    continue;
                }

                var closingLength = 1;
                while (cursor + closingLength < markdown.Length && markdown[cursor + closingLength] == '`')
                    closingLength++;

                if (closingLength == openingLength)
                {
                    closingEnd = cursor + closingLength;
                    break;
                }

                cursor += closingLength;
            }

            if (closingEnd < 0)
            {
                visible.Append('`', openingLength);
                index += openingLength;
            }
            else
            {
                visible.Append(' ');
                index = closingEnd;
            }
        }

        return visible.ToString();
    }

    private static IEnumerable<string> ExtractHtmlDestinations(string markdown)
    {
        var withoutComments = Regex.Replace(
            markdown,
            @"<!--.*?-->",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        for (var index = 0; index < withoutComments.Length; index++)
        {
            if (withoutComments[index] != '<' ||
                index + 1 >= withoutComments.Length ||
                !char.IsLetter(withoutComments[index + 1]))
            {
                continue;
            }

            var cursor = index + 1;
            while (cursor < withoutComments.Length &&
                   (char.IsLetterOrDigit(withoutComments[cursor]) || withoutComments[cursor] is ':' or '-'))
            {
                cursor++;
            }

            var tagEnd = cursor;
            var quote = '\0';
            for (; tagEnd < withoutComments.Length; tagEnd++)
            {
                var character = withoutComments[tagEnd];
                if (quote != '\0')
                {
                    if (character == quote)
                        quote = '\0';
                }
                else if (character is '\'' or '"')
                {
                    quote = character;
                }
                else if (character == '>')
                {
                    break;
                }
            }

            if (tagEnd >= withoutComments.Length)
                yield break;

            while (cursor < tagEnd)
            {
                while (cursor < tagEnd && (char.IsWhiteSpace(withoutComments[cursor]) || withoutComments[cursor] == '/'))
                    cursor++;

                var nameStart = cursor;
                while (cursor < tagEnd &&
                       !char.IsWhiteSpace(withoutComments[cursor]) &&
                       withoutComments[cursor] is not '=' and not '/')
                {
                    cursor++;
                }

                if (cursor == nameStart)
                    break;

                var name = withoutComments.Substring(nameStart, cursor - nameStart);
                while (cursor < tagEnd && char.IsWhiteSpace(withoutComments[cursor]))
                    cursor++;

                if (cursor >= tagEnd || withoutComments[cursor] != '=')
                    continue;

                cursor++;
                while (cursor < tagEnd && char.IsWhiteSpace(withoutComments[cursor]))
                    cursor++;

                var valueStart = cursor;
                var valueLength = 0;
                if (cursor < tagEnd && withoutComments[cursor] is '\'' or '"')
                {
                    var valueQuote = withoutComments[cursor++];
                    valueStart = cursor;
                    while (cursor < tagEnd && withoutComments[cursor] != valueQuote)
                        cursor++;
                    valueLength = cursor - valueStart;
                    if (cursor < tagEnd)
                        cursor++;
                }
                else
                {
                    while (cursor < tagEnd && !char.IsWhiteSpace(withoutComments[cursor]))
                        cursor++;
                    valueLength = cursor - valueStart;
                }

                if (valueLength > 0 &&
                    (name.Equals("href", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("src", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return withoutComments.Substring(valueStart, valueLength);
                }
            }

            index = tagEnd;
        }
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
