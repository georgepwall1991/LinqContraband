using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using LinqContraband.Catalog;
using Microsoft.CodeAnalysis;
using Xunit;

namespace LinqContraband.Tests.Architecture;

/// <summary>
/// The opt-in severity presets (<c>&lt;LinqContrabandPreset&gt;</c>) ship as global analyzer configs in the package's
/// <c>build/</c> folder. These tests pin the generated files to the catalog, prove the package wiring, and run each
/// preset through the compiler's own analyzer-config handling.
/// </summary>
public sealed class SeverityPresetTests
{
    private readonly string _repoRoot = RepositoryLayout.GetRepositoryRoot();

    private string BuildDirectory => Path.Combine(_repoRoot, "src", "LinqContraband", "build");

    [Fact]
    public void PresetFiles_MatchTheCatalog()
    {
        foreach (var preset in RuleCatalogPresets.All)
        {
            var path = Path.Combine(BuildDirectory, "presets", preset.FileName);
            Assert.True(File.Exists(path), $"Missing {path}. Run the RuleCatalogDocGenerator with --write.");
            Assert.Equal(preset.ToGlobalConfig(), File.ReadAllText(path).Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public void Presets_HaveDistinctNamesAndGlobalLevelsBelowUserConfigs()
    {
        var presets = RuleCatalogPresets.All;
        Assert.Equal(presets.Length, presets.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(presets.Length, presets.Select(preset => preset.GlobalLevel).Distinct().Count());
        Assert.All(presets, preset => Assert.True(preset.GlobalLevel < 100, $"{preset.Name} must stay below a user .globalconfig (100)."));
        Assert.All(presets, preset => Assert.Equal(preset.Name.ToLowerInvariant(), preset.Name));
    }

    [Fact]
    public void Presets_OnlyNameCatalogRules()
    {
        var ids = RuleCatalog.All.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var preset in RuleCatalogPresets.All)
        {
            Assert.NotEmpty(preset.Severities);
            Assert.All(preset.Severities, entry => Assert.Contains(entry.RuleId, ids));
            Assert.All(preset.Severities, entry => Assert.Contains(entry.Severity, new[] { "error", "warning", "suggestion", "silent", "none" }));
        }
    }

    [Fact]
    public void StrictPreset_CoversEveryRule()
    {
        var strict = RuleCatalogPresets.All.Single(preset => preset.Name == "strict");
        Assert.Equal(
            RuleCatalog.All.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal),
            strict.Severities.Select(entry => entry.RuleId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Targets_WireEveryPresetIntoTheCompilation()
    {
        var targets = XDocument.Load(Path.Combine(BuildDirectory, "LinqContraband.targets"));
        var includes = targets.Descendants("EditorConfigFiles").ToArray();

        foreach (var preset in RuleCatalogPresets.All)
        {
            var include = includes.SingleOrDefault(item =>
                (string?)item.Attribute("Include") == "$(MSBuildThisFileDirectory)presets/" + preset.FileName);
            Assert.True(include != null, $"LinqContraband.targets does not add {preset.FileName}.");
            Assert.Contains($"';{preset.Name};'", (string?)include!.Attribute("Condition") ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Package_PacksTargetsAndPresets()
    {
        var project = XDocument.Load(Path.Combine(_repoRoot, "src", "LinqContraband", "LinqContraband.csproj"));
        var packed = project.Descendants("None")
            .Where(item => (string?)item.Attribute("Pack") == "true")
            .Select(item => ((string?)item.Attribute("Update"), (string?)item.Attribute("PackagePath")))
            .ToArray();

        Assert.Contains((@"build\LinqContraband.targets", "build/%(Filename)%(Extension)"), packed);
        Assert.Contains((@"build\presets\*.globalconfig", "build/presets/%(Filename)%(Extension)"), packed);
    }

    [Theory]
    [InlineData("security", "LC018", ReportDiagnostic.Error)]
    [InlineData("security", "LC037", ReportDiagnostic.Error)]
    [InlineData("critical", "LC044", ReportDiagnostic.Error)]
    [InlineData("critical", "LC034", ReportDiagnostic.Error)]
    [InlineData("strict", "LC005", ReportDiagnostic.Error)]
    [InlineData("strict", "LC029", ReportDiagnostic.Warn)]
    [InlineData("essentials", "LC029", ReportDiagnostic.Suppress)]
    public void Compiler_ResolvesPresetSeverity(string presetName, string ruleId, ReportDiagnostic expected)
    {
        var options = Resolve(Preset(presetName));

        Assert.Empty(options.Diagnostics);
        Assert.True(options.TreeOptions.TryGetValue(ruleId, out var actual), $"{presetName} does not set {ruleId}.");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Compiler_LeavesRulesOutsideAPresetAlone()
    {
        var options = Resolve(Preset("security"));

        Assert.False(options.TreeOptions.ContainsKey("LC005"));
        Assert.False(options.TreeOptions.ContainsKey("LC044"));
    }

    [Fact]
    public void Compiler_CombinesPresetsWithoutConflicts()
    {
        // Each preset has its own global_level, so overlapping keys resolve to the higher level instead of the
        // compiler reporting a same-level conflict and unsetting the key.
        var options = Resolve(Preset("security"), Preset("critical"), Preset("strict"), Preset("essentials"));

        Assert.Empty(options.Diagnostics);
        Assert.Equal(ReportDiagnostic.Error, options.TreeOptions["LC018"]);
        Assert.Equal(ReportDiagnostic.Error, options.TreeOptions["LC044"]);
        Assert.Equal(ReportDiagnostic.Error, options.TreeOptions["LC005"]);
        Assert.Equal(ReportDiagnostic.Warn, options.TreeOptions["LC029"]);
    }

    [Fact]
    public void Compiler_LetsAUserGlobalConfigOverrideAPreset()
    {
        var userGlobal = AnalyzerConfig.Parse(
            "is_global = true\ndotnet_diagnostic.LC005.severity = suggestion\n",
            "/repo/.globalconfig");

        var options = Resolve(Preset("strict"), userGlobal);

        Assert.Empty(options.Diagnostics);
        Assert.Equal(ReportDiagnostic.Info, options.TreeOptions["LC005"]);
    }

    private static AnalyzerConfig Preset(string name)
    {
        var preset = RuleCatalogPresets.All.Single(candidate => candidate.Name == name);
        return AnalyzerConfig.Parse(preset.ToGlobalConfig(), "/packages/linqcontraband/build/presets/" + preset.FileName);
    }

    private static AnalyzerConfigOptionsResult Resolve(params AnalyzerConfig[] configs)
    {
        // Severities from global configs apply to every file, so the compiler keeps them in GlobalConfigOptions.
        return AnalyzerConfigSet.Create(configs).GlobalConfigOptions;
    }
}
