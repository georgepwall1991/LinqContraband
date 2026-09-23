using System.Security;

namespace LinqContraband.Scan;

/// <summary>
/// The MSBuild file the scan injects through <c>CustomAfterMicrosoftCommonTargets</c>. It swaps any
/// LinqContraband analyzer the project already references for the scanner's copy (so rules are not
/// reported twice or by an older version) and gives every compilation its own SARIF error log. One shared
/// <c>ErrorLog</c> path would be overwritten by each compilation, and even a name per project and target
/// framework collides when a solution builds one project twice with different global properties.
/// </summary>
internal static class ScanTargets
{
    public static string Create(string analyzerAssemblyPath, string errorLogDirectory)
    {
        var analyzer = SecurityElement.Escape(Path.GetFullPath(analyzerAssemblyPath));
        var logDirectory = SecurityElement.Escape(Path.GetFullPath(errorLogDirectory));

        return $"""
            <Project>
              <!-- Written by linqcontraband-scan for one scan and deleted afterwards. -->
              <Target Name="_LinqContrabandScanInject" BeforeTargets="CoreCompile" Condition="'$(Language)' == 'C#'">
                <ItemGroup>
                  <Analyzer Remove="@(Analyzer)" Condition="'%(Filename)' == 'LinqContraband'" />
                  <Analyzer Include="{analyzer}" />
                </ItemGroup>
                <PropertyGroup>
                  <ErrorLog>{logDirectory}{Path.DirectorySeparatorChar}$(MSBuildProjectName).$([System.Guid]::NewGuid().ToString('N')).sarif,version=2.1</ErrorLog>
                </PropertyGroup>
              </Target>
            </Project>
            """;
    }
}
