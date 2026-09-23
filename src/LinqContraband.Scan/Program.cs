using System.Reflection;
using System.Text.RegularExpressions;
using LinqContraband.Scan;

return ScanCommand.Run(args, Console.Out, Console.Error);

namespace LinqContraband.Scan
{
    internal static class ScanCommand
    {
        public const int Success = 0;
        public const int UsageError = 2;
        public const int ScanFailed = 3;

        public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, string? analyzerAssemblyPath = null)
        {
            if (!ScanOptions.TryParse(args, out var options, out var parseError))
            {
                error.WriteLine(parseError);
                error.WriteLine("Run 'linqcontraband-scan --help' for usage.");
                return UsageError;
            }

            if (options.ShowHelp)
            {
                output.WriteLine(ScanOptions.Usage);
                return Success;
            }

            var version = ToolVersion;
            if (options.ShowVersion)
            {
                output.WriteLine($"linqcontraband-scan {version}");
                return Success;
            }

            if (!File.Exists(options.Target) && !Directory.Exists(options.Target))
            {
                error.WriteLine($"Nothing to scan at '{options.Target}'. Pass a solution, project or directory.");
                return UsageError;
            }

            analyzerAssemblyPath ??= Path.Combine(AppContext.BaseDirectory, "analyzer", "LinqContraband.dll");
            if (!File.Exists(analyzerAssemblyPath))
            {
                error.WriteLine($"The LinqContraband analyzer is missing from the tool at '{analyzerAssemblyPath}'. Reinstall LinqContraband.Scan.");
                return ScanFailed;
            }

            output.WriteLine($"Building {Path.GetFullPath(options.Target)} with LinqContraband {version}...");
            var result = Scanner.Run(options, analyzerAssemblyPath, options.Verbose ? output.WriteLine : null);

            if (result.BuildExitCode != 0)
            {
                if (!options.Verbose)
                    error.WriteLine(BuildErrorExcerpt(result.BuildOutput));
                if (result.ErrorLogCount == 0)
                {
                    error.WriteLine("The build failed before any project was compiled, so nothing was scanned. Fix the build (run 'dotnet build' to see why) and try again.");
                    return ScanFailed;
                }
                error.WriteLine(RuleErrorHint(result.BuildOutput) ?? "The build failed, so projects after the failure were not scanned. Results below are partial.");
            }
            else if (result.ErrorLogCount == 0)
            {
                error.WriteLine("No C# project was compiled, so nothing was scanned. Point the scanner at a solution or C# project.");
                return ScanFailed;
            }

            if (result.UnreadableLogCount > 0)
                error.WriteLine($"{result.UnreadableLogCount} of {result.ErrorLogCount} compiler logs could not be read, so their projects are missing from the results.");

            var sarifPath = Path.GetFullPath(options.SarifPath);
            var sarifDirectory = Path.GetDirectoryName(sarifPath);
            if (!string.IsNullOrEmpty(sarifDirectory))
                Directory.CreateDirectory(sarifDirectory);
            using (var stream = File.Create(sarifPath))
                result.Report.WriteSarif(stream, version);

            output.WriteLine();
            output.Write(result.Report.RenderText(options.Top, DisplayPath(sarifPath)));
            return result.BuildExitCode == 0 ? Success : ScanFailed;
        }

        internal static string ToolVersion =>
            typeof(ScanCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(ScanCommand).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";

        /// <summary>
        /// Explains a build that failed on a finding: a rule set to <c>error</c> in <c>.editorconfig</c> fails that
        /// project's build, and the projects that depend on it are not built or scanned. The scan cannot lower those
        /// severities (presets it can, and does), so it says which rules to set to <c>warning</c>.
        /// </summary>
        internal static string? RuleErrorHint(string buildOutput)
        {
            var rules = Regex.Matches(buildOutput, @": error (LC\d{3}|EF100[23]):")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            return rules.Count == 0
                ? null
                : $"The build failed because {string.Join(", ", rules)} {(rules.Count == 1 ? "is" : "are")} set to error severity, so projects that depend on a failing project were not scanned. Results below are partial. Set {(rules.Count == 1 ? "it" : "them")} to warning in .editorconfig to scan everything.";
        }

        /// <summary>A path relative to the current directory when it is inside it, otherwise the full path.</summary>
        internal static string DisplayPath(string fullPath)
        {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? fullPath
                : relative;
        }

        /// <summary>The error lines of a failed quiet build, which is all a user needs to see why it failed.</summary>
        internal static string BuildErrorExcerpt(string buildOutput)
        {
            var errors = buildOutput
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Contains(": error ", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToList();

            return errors.Count > 0
                ? "dotnet build reported errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(line => "  " + line))
                : "dotnet build failed. Run again with --verbose to see its output.";
        }
    }
}
