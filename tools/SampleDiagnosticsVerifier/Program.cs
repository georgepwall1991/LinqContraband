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
var safeSamplePaths = LoadSafeSamplePaths(sourceSampleManifestPath, sourceSampleProjectDirectory);

if (expectedSampleGroups.Length == 0)
{
    Console.Error.WriteLine($"No sample diagnostic expectations found in {sourceSampleManifestPath}");
    return 1;
}

Console.WriteLine($"Verifying {expectedSampleGroups.Sum(group => group.SamplePaths.Count)} diagnostic sample files and {safeSamplePaths.Length} safe sample files across {expectedSampleGroups.Length} diagnostic paths in {sourceSampleProjectPath}");

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
        var safePaths = safeSamplePaths.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in observedByPath.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (safePaths.Contains(pair.Key))
            {
                failures.Add($"{pair.Key} is listed as a safe sample but reported diagnostics [{string.Join(", ", pair.Value)}].");
            }
            else if (!expectedPaths.Contains(pair.Key))
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

internal sealed record Options
{
    public List<string> Frameworks { get; } = new();
    public string Configuration { get; set; } = "Debug";
    public bool NoRestore { get; set; }
}
