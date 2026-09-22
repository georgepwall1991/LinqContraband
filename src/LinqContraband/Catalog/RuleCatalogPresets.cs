using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Catalog;

/// <summary>
/// One opt-in severity preset. The package ships each preset as a global analyzer config under
/// <c>build/presets/</c>; <c>build/LinqContraband.targets</c> adds it to the compilation when the project sets
/// <c>&lt;LinqContrabandPreset&gt;</c> to the preset's name. Presets only set severities, so a project's own
/// <c>.editorconfig</c> entries still win over them.
/// </summary>
public sealed class RuleCatalogPreset
{
    public RuleCatalogPreset(string name, int globalLevel, string summary, ImmutableArray<(string RuleId, string Severity)> severities)
    {
        Name = name;
        GlobalLevel = globalLevel;
        Summary = summary;
        Severities = severities;
    }

    /// <summary>The value users put in <c>&lt;LinqContrabandPreset&gt;</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The <c>global_level</c> of the generated config. Every preset has a distinct level below the default level
    /// of a user's own <c>.globalconfig</c> (100), so combining presets never produces a same-level key conflict,
    /// and user global configs still override them.
    /// </summary>
    public int GlobalLevel { get; }

    public string Summary { get; }

    public ImmutableArray<(string RuleId, string Severity)> Severities { get; }

    public string FileName => "LinqContraband." + Name + ".globalconfig";

    public string ToGlobalConfig()
    {
        var builder = new StringBuilder();
        builder.Append("# LinqContraband '").Append(Name).Append("' preset: ").Append(Summary).Append('\n');
        builder.Append("# Generated from RuleCatalogPresets by tools/RuleCatalogDocGenerator; do not edit by hand.\n");
        builder.Append("# Enable it with <LinqContrabandPreset>").Append(Name).Append("</LinqContrabandPreset> in your project file.\n");
        builder.Append("is_global = true\n");
        builder.Append("global_level = ").Append(GlobalLevel).Append('\n');
        builder.Append('\n');
        foreach (var (ruleId, severity) in Severities)
            builder.Append("dotnet_diagnostic.").Append(ruleId).Append(".severity = ").Append(severity).Append('\n');

        return builder.ToString();
    }
}

public static class RuleCatalogPresets
{
    /// <summary>SQL injection: raw SQL built from interpolated, concatenated, or constructed strings.</summary>
    public static ImmutableArray<string> SecurityRuleIds { get; } = ImmutableArray.Create("LC018", "LC034", "LC037");

    /// <summary>
    /// Security plus the rules whose findings are runtime failures or silent data loss rather than slow queries:
    /// disposed-context queries, always-throwing conditional includes, contexts shared across threads or
    /// concurrent operations, lost AsNoTracking writes, ExecuteDelete skipping soft-delete/cascade, and lost updates.
    /// </summary>
    public static ImmutableArray<string> CriticalRuleIds { get; } = SecurityRuleIds.AddRange(
        new[] { "LC013", "LC019", "LC036", "LC044", "LC046", "LC047", "LC048" });

    public static ImmutableArray<RuleCatalogPreset> All { get; } = ImmutableArray.Create(
        new RuleCatalogPreset(
            "security",
            -10,
            "SQL injection rules are build errors.",
            SecurityRuleIds.Select(id => (id, "error")).ToImmutableArray()),
        new RuleCatalogPreset(
            "critical",
            -20,
            "SQL injection, runtime-failure, and silent data-loss rules are build errors.",
            CriticalRuleIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => (id, "error")).ToImmutableArray()),
        new RuleCatalogPreset(
            "strict",
            -30,
            "every warning rule is a build error and every advisory (Info) rule is a warning.",
            RuleCatalog.All
                .Select(rule => (rule.Id, rule.Severity == DiagnosticSeverity.Warning ? "error" : "warning"))
                .ToImmutableArray()),
        new RuleCatalogPreset(
            "essentials",
            -40,
            "advisory (Info) rules are turned off; warning rules keep their defaults.",
            RuleCatalog.All
                .Where(rule => rule.Severity == DiagnosticSeverity.Info)
                .Select(rule => (rule.Id, "none"))
                .ToImmutableArray()));
}
