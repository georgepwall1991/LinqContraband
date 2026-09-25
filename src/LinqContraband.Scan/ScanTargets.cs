using System.Security;

namespace LinqContraband.Scan;

/// <summary>
/// The MSBuild file the scan injects through <c>CustomAfterMicrosoftCommonTargets</c>. It swaps any
/// LinqContraband analyzer the project already references for the scanner's copy (so rules are not
/// reported twice or by an older version) and gives every compilation its own SARIF error log. One shared
/// <c>ErrorLog</c> path would be overwritten by each compilation, and even a name per project and target
/// framework collides when a solution builds one project twice with different global properties.
/// It also hands the compiler <see cref="GlobalConfig"/>, which keeps severities a repository sets for all
/// analyzers or a whole category from turning the findings into build errors.
/// </summary>
internal static class ScanTargets
{
    public const string TargetsFileName = "LinqContraband.Scan.targets";
    public const string GlobalConfigFileName = "LinqContraband.Scan.globalconfig";

    /// <summary>
    /// Every LinqContraband rule, in catalog order, and EF Core's EF1002 and EF1003, which the report includes.
    /// A test keeps the LinqContraband part equal to the rule catalog.
    /// </summary>
    public static readonly IReadOnlyList<string> ReportedRules =
    [
        "LC001", "LC002", "LC003", "LC004", "LC005", "LC006", "LC007", "LC008", "LC009", "LC010",
        "LC011", "LC012", "LC013", "LC014", "LC015", "LC016", "LC017", "LC018", "LC019", "LC020",
        "LC021", "LC022", "LC023", "LC024", "LC025", "LC026", "LC027", "LC028", "LC029", "LC030",
        "LC031", "LC032", "LC033", "LC034", "LC035", "LC036", "LC037", "LC038", "LC039", "LC040",
        "LC041", "LC042", "LC043", "LC044", "LC045", "LC046", "LC047", "LC048", "LC049", "LC050",
        "LC051", "LC052", "LC053", "LC054", "LC055", "LC056",
        "EF1002", "EF1003",
    ];

    /// <summary>
    /// Writes the targets file and the global config it passes to the compiler into <paramref name="workDirectory"/>
    /// and returns the targets file's path.
    /// </summary>
    public static string Write(string workDirectory, string analyzerAssemblyPath, string errorLogDirectory)
    {
        var globalConfigPath = Path.Combine(workDirectory, GlobalConfigFileName);
        File.WriteAllText(globalConfigPath, GlobalConfig());

        var targetsPath = Path.Combine(workDirectory, TargetsFileName);
        File.WriteAllText(targetsPath, Create(analyzerAssemblyPath, errorLogDirectory, globalConfigPath));
        return targetsPath;
    }

    public static string Create(string analyzerAssemblyPath, string errorLogDirectory, string globalConfigPath)
    {
        var analyzer = SecurityElement.Escape(Path.GetFullPath(analyzerAssemblyPath));
        var logDirectory = SecurityElement.Escape(Path.GetFullPath(errorLogDirectory));
        var globalConfig = SecurityElement.Escape(Path.GetFullPath(globalConfigPath));

        return $"""
            <Project>
              <!-- Written by linqcontraband-scan for one scan and deleted afterwards. -->
              <Target Name="_LinqContrabandScanInject" BeforeTargets="CoreCompile" Condition="'$(Language)' == 'C#'">
                <ItemGroup>
                  <Analyzer Remove="@(Analyzer)" Condition="'%(Filename)' == 'LinqContraband'" />
                  <Analyzer Include="{analyzer}" />
                  <EditorConfigFiles Include="{globalConfig}" />
                </ItemGroup>
                <PropertyGroup>
                  <ErrorLog>{logDirectory}{Path.DirectorySeparatorChar}$(MSBuildProjectName).$([System.Guid]::NewGuid().ToString('N')).sarif,version=2.1</ErrorLog>
                </PropertyGroup>
              </Target>
            </Project>
            """;
    }

    /// <summary>
    /// A global analyzer config that sets each reported rule to its <c>default</c> severity. A setting for one rule
    /// id wins over <c>dotnet_analyzer_diagnostic.severity</c> and <c>dotnet_analyzer_diagnostic.category-*.severity</c>,
    /// so a repository that raises every analyzer diagnostic to <c>error</c> does not fail the scan build on the
    /// findings, and the projects that depend on it are still built and scanned. The repository's own settings for a
    /// rule id still win: an <c>.editorconfig</c> entry always beats a global config, and the lowest global level
    /// loses to any global config the repository has.
    /// </summary>
    public static string GlobalConfig()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("# Written by linqcontraband-scan for one scan and deleted afterwards.");
        text.AppendLine("is_global = true");
        text.AppendLine("global_level = -1000");
        foreach (var rule in ReportedRules)
            text.AppendLine($"dotnet_diagnostic.{rule}.severity = default");
        return text.ToString();
    }
}
