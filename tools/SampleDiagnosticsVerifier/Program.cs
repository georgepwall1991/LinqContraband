using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

var repoRoot = FindRepoRoot();
var sourceSampleProjectDirectory = Path.Combine(repoRoot, "samples", "LinqContraband.Sample");
var sourceSampleProjectPath = Path.Combine(sourceSampleProjectDirectory, "LinqContraband.Sample.csproj");
var sourceSampleManifestPath = Path.Combine(sourceSampleProjectDirectory, "sample-diagnostics.json");

var options = ParseArgs(args);
var frameworks = options.Frameworks.Count == 0 ? new[] { "net8.0", "net9.0", "net10.0" } : options.Frameworks.ToArray();

if (!File.Exists(sourceSampleProjectPath))
{
    Console.Error.WriteLine($"Sample project not found: {sourceSampleProjectPath}");
    return 1;
}

if (!File.Exists(sourceSampleManifestPath))
{
    Console.Error.WriteLine($"Sample diagnostic manifest not found: {sourceSampleManifestPath}");
    return 1;
}

var expectedSampleGroups = LoadExpectationGroups(sourceSampleManifestPath, sourceSampleProjectDirectory);

if (expectedSampleGroups.Length == 0)
{
    Console.Error.WriteLine($"No sample diagnostic expectations found in {sourceSampleManifestPath}");
    return 1;
}

Console.WriteLine($"Verifying {expectedSampleGroups.Sum(group => group.SamplePaths.Count)} sample files across {expectedSampleGroups.Length} diagnostic paths in {sourceSampleProjectPath}");

var tempRoot = Path.Combine(Path.GetTempPath(), "LinqContraband.SampleDiagnosticsVerifier", Guid.NewGuid().ToString("N"));
var tempSampleProjectDirectory = Path.Combine(tempRoot, "LinqContraband.Sample");
var tempSampleProjectPath = Path.Combine(tempSampleProjectDirectory, "LinqContraband.Sample.csproj");

try
{
    CopyDirectory(sourceSampleProjectDirectory, tempSampleProjectDirectory);
    RewriteAnalyzerProjectReference(tempSampleProjectPath, Path.Combine(repoRoot, "src", "LinqContraband", "LinqContraband.csproj"));
    RunDotnetRestore(tempSampleProjectPath, tempSampleProjectDirectory);

    foreach (var framework in frameworks)
    {
        Console.WriteLine();
        Console.WriteLine($"[{framework}] rebuilding sample project and collecting diagnostics...");

        var errorLogPath = Path.Combine(tempSampleProjectDirectory, $"sample-diagnostics-{framework}.sarif");
        File.WriteAllText(errorLogPath, string.Empty);

        RunDotnetBuild(tempSampleProjectPath, tempSampleProjectDirectory, framework, options.Configuration, errorLogPath);
        var diagnostics = ParseDiagnostics(errorLogPath, tempSampleProjectDirectory);
        var observedByPath = diagnostics
            .GroupBy(diagnostic => diagnostic.RelativePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(diagnostic => diagnostic.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var failures = new List<string>();

        foreach (var expectedGroup in expectedSampleGroups)
        {
            observedByPath.TryGetValue(expectedGroup.DiagnosticPath, out var observedIds);
            observedIds ??= Array.Empty<string>();

            var missingIds = expectedGroup.ExpectedRuleIds.Except(observedIds, StringComparer.Ordinal).ToArray();
            var unexpectedIds = observedIds.Except(expectedGroup.ExpectedRuleIds, StringComparer.Ordinal).ToArray();

            if (missingIds.Length > 0)
            {
                failures.Add(
                    $"{expectedGroup.DiagnosticPath} missing expected diagnostics [{string.Join(", ", missingIds)}]; expected: {string.Join(", ", expectedGroup.ExpectedRuleIds)}; observed: {(observedIds.Length == 0 ? "<none>" : string.Join(", ", observedIds))}; samples: {string.Join(", ", expectedGroup.SamplePaths)}");
            }

            if (unexpectedIds.Length > 0)
            {
                failures.Add(
                    $"{expectedGroup.DiagnosticPath} reported unexpected diagnostics [{string.Join(", ", unexpectedIds)}]; expected: {string.Join(", ", expectedGroup.ExpectedRuleIds)}; observed: {string.Join(", ", observedIds)}; samples: {string.Join(", ", expectedGroup.SamplePaths)}");
            }
        }

        var expectedPaths = expectedSampleGroups.Select(group => group.DiagnosticPath).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in observedByPath.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!expectedPaths.Contains(pair.Key))
            {
                failures.Add($"{pair.Key} reported unexpected diagnostics [{string.Join(", ", pair.Value)}] with no matching sample expectation.");
            }
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"[{framework}] sample diagnostic verification failed:");
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }

            return 1;
        }

        Console.WriteLine($"[{framework}] verified {expectedSampleGroups.Length} diagnostic paths.");
    }
}
finally
{
    if (Directory.Exists(tempRoot))
    {
        Directory.Delete(tempRoot, recursive: true);
    }
}

Console.WriteLine();
Console.WriteLine("Sample diagnostic verification passed.");
return 0;

static string FindRepoRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "LinqContraband.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not locate LinqContraband.sln from the current working directory.");
}

static Options ParseArgs(string[] args)
{
    var options = new Options();

    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        switch (arg)
        {
            case "--framework":
            case "--frameworks":
                index++;
                while (index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    options.Frameworks.Add(args[index]);
                    index++;
                }

                index--;
                break;

            case "--configuration":
                options.Configuration = args[++index];
                break;

            case "--no-restore":
                options.NoRestore = true;
                break;

            default:
                throw new ArgumentException($"Unknown argument '{arg}'.");
        }
    }

    return options;
}

static string RunDotnetBuild(string sampleProjectPath, string sampleProjectDirectory, string framework, string configuration, string errorLogPath)
{
    var arguments = new List<string>()
    {
        "build",
        Quote(sampleProjectPath),
        "-v:normal",
        "-f",
        framework,
        "-c",
        configuration,
        "/p:GenerateFullPaths=true",
        "/p:GeneratePackageOnBuild=false",
        "/p:ErrorLog=" + Quote(errorLogPath),
        "--no-restore"
    };

    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = string.Join(" ", arguments),
        WorkingDirectory = sampleProjectDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();

    process.WaitForExit();

    var combinedOutput = stdout;
    if (!string.IsNullOrWhiteSpace(stderr))
    {
        combinedOutput += Environment.NewLine + stderr;
    }

    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine(combinedOutput);
        throw new InvalidOperationException($"dotnet build failed for {framework} with exit code {process.ExitCode}.");
    }

    return combinedOutput;
}

static void RunDotnetRestore(string sampleProjectPath, string sampleProjectDirectory)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"restore {Quote(sampleProjectPath)} -v:minimal",
        WorkingDirectory = sampleProjectDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet restore.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();

    process.WaitForExit();

    if (process.ExitCode == 0)
        return;

    var combinedOutput = stdout;
    if (!string.IsNullOrWhiteSpace(stderr))
    {
        combinedOutput += Environment.NewLine + stderr;
    }

    Console.Error.WriteLine(combinedOutput);
    throw new InvalidOperationException($"dotnet restore failed with exit code {process.ExitCode}.");
}

static IReadOnlyList<SampleDiagnostic> ParseDiagnostics(string errorLogPath, string sampleProjectDirectory)
{
    using var stream = File.OpenRead(errorLogPath);
    using var document = JsonDocument.Parse(stream);

    var diagnostics = new List<SampleDiagnostic>();

    if (!document.RootElement.TryGetProperty("runs", out var runs))
    {
        return diagnostics;
    }

    foreach (var run in runs.EnumerateArray())
    {
        if (!run.TryGetProperty("results", out var results))
        {
            continue;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("ruleId", out var ruleIdProperty))
            {
                continue;
            }

            var ruleId = ruleIdProperty.GetString();
            if (string.IsNullOrWhiteSpace(ruleId) || !Regex.IsMatch(ruleId, @"^LC\d{3}$"))
            {
                continue;
            }

            if (!TryGetDiagnosticPath(result, out var path))
            {
                continue;
            }

            diagnostics.Add(new SampleDiagnostic(NormalizeRelativePath(sampleProjectDirectory, path), ruleId));
        }
    }

    return diagnostics;
}

static bool TryGetDiagnosticPath(JsonElement result, out string path)
{
    path = string.Empty;

    if (!result.TryGetProperty("locations", out var locations) || locations.GetArrayLength() == 0)
    {
        return false;
    }

    var location = locations[0];
    JsonElement uriProperty;

    if (location.TryGetProperty("resultFile", out var resultFile) &&
        resultFile.TryGetProperty("uri", out uriProperty))
    {
        return TryNormalizeUriPath(uriProperty, out path);
    }

    if (!location.TryGetProperty("physicalLocation", out var physicalLocation) ||
        !physicalLocation.TryGetProperty("artifactLocation", out var artifactLocation) ||
        !artifactLocation.TryGetProperty("uri", out uriProperty))
    {
        return false;
    }

    return TryNormalizeUriPath(uriProperty, out path);
}

static bool TryNormalizeUriPath(JsonElement uriProperty, out string path)
{
    path = string.Empty;

    var uriText = uriProperty.GetString();
    if (string.IsNullOrWhiteSpace(uriText))
    {
        return false;
    }

    if (Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && uri.IsFile)
    {
        path = uri.LocalPath;
        return true;
    }

    path = uriText;
    return true;
}

static SampleExpectationGroup[] LoadExpectationGroups(string manifestPath, string sampleProjectDirectory)
{
    using var stream = File.OpenRead(manifestPath);
    using var document = JsonDocument.Parse(stream);
    var expectations = new List<SampleExpectation>();

    foreach (var expectation in document.RootElement.GetProperty("expectations").EnumerateArray())
    {
        var diagnosticPath = expectation.GetProperty("diagnosticPath").GetString();
        if (string.IsNullOrWhiteSpace(diagnosticPath))
        {
            throw new InvalidOperationException($"{manifestPath} contains an expectation with no diagnosticPath.");
        }

        var samplePaths = expectation.GetProperty("samplePaths")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedRuleIds = expectation.GetProperty("ruleIds")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (samplePaths.Length == 0 || expectedRuleIds.Length == 0)
        {
            throw new InvalidOperationException($"{manifestPath} expectation '{diagnosticPath}' must declare samplePaths and ruleIds.");
        }

        if (!File.Exists(Path.Combine(sampleProjectDirectory, diagnosticPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            throw new InvalidOperationException($"{manifestPath} diagnosticPath does not exist: {diagnosticPath}");
        }

        foreach (var samplePath in samplePaths)
        {
            if (!File.Exists(Path.Combine(sampleProjectDirectory, samplePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                throw new InvalidOperationException($"{manifestPath} samplePath does not exist: {samplePath}");
            }
        }

        expectations.Add(new SampleExpectation(diagnosticPath, samplePaths, expectedRuleIds));
    }

    return expectations
        .GroupBy(sample => sample.DiagnosticPath, StringComparer.Ordinal)
        .Select(group => new SampleExpectationGroup(
            group.Key,
            group.SelectMany(sample => sample.SamplePaths).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            group.SelectMany(sample => sample.ExpectedRuleIds).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()))
        .OrderBy(group => group.DiagnosticPath, StringComparer.Ordinal)
        .ToArray();
}

static string Quote(string value)
{
    return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}

static void RewriteAnalyzerProjectReference(string projectPath, string analyzerProjectPath)
{
    var projectXml = File.ReadAllText(projectPath);
    var updatedXml = projectXml.Replace(@"..\..\src\LinqContraband\LinqContraband.csproj", analyzerProjectPath, StringComparison.Ordinal);
    File.WriteAllText(projectPath, updatedXml);
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory)
{
    Directory.CreateDirectory(destinationDirectory);

    foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory);

        if (ContainsBuildArtifactSegment(relativeDirectory))
        {
            continue;
        }

        Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
    }

    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativeFile = Path.GetRelativePath(sourceDirectory, file);

        if (ContainsBuildArtifactSegment(relativeFile))
        {
            continue;
        }

        var destinationFile = Path.Combine(destinationDirectory, relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        File.Copy(file, destinationFile, overwrite: true);
    }
}

static bool ContainsBuildArtifactSegment(string relativePath)
{
    return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
}

static string NormalizeRelativePath(string sampleProjectDirectory, string path)
{
    var normalizedRoot = sampleProjectDirectory.Replace('\\', '/').TrimEnd('/');
    var normalizedPath = path.Replace('\\', '/');

    if (normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
    {
        return normalizedPath[(normalizedRoot.Length + 1)..];
    }

    var privateRoot = "/private" + normalizedRoot;
    if (normalizedPath.StartsWith(privateRoot + "/", StringComparison.OrdinalIgnoreCase))
    {
        return normalizedPath[(privateRoot.Length + 1)..];
    }

    return normalizedPath;
}

internal sealed record Options
{
    public List<string> Frameworks { get; } = new();
    public string Configuration { get; set; } = "Debug";
    public bool NoRestore { get; set; }
}

internal sealed record SampleExpectation(string DiagnosticPath, IReadOnlyList<string> SamplePaths, IReadOnlyList<string> ExpectedRuleIds);

internal sealed record SampleExpectationGroup(string DiagnosticPath, IReadOnlyList<string> SamplePaths, IReadOnlyList<string> ExpectedRuleIds);

internal sealed record SampleDiagnostic(string RelativePath, string Id);
