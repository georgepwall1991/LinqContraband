using System.Text.Json;

internal partial class Program
{
    private static SampleExpectationGroup[] LoadExpectationGroups(string manifestPath, string sampleProjectDirectory)
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

    private static string[] LoadSafeSamplePaths(string manifestPath, string sampleProjectDirectory)
    {
        using var stream = File.OpenRead(manifestPath);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("safeSamples", out var safeSamplesElement))
        {
            throw new InvalidOperationException($"{manifestPath} must declare safeSamples for false-positive regression coverage.");
        }

        var safeSamplePaths = safeSamplesElement
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (safeSamplePaths.Length == 0)
        {
            throw new InvalidOperationException($"{manifestPath} must declare at least one safeSamples path.");
        }

        foreach (var safeSamplePath in safeSamplePaths)
        {
            if (!File.Exists(Path.Combine(sampleProjectDirectory, safeSamplePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                throw new InvalidOperationException($"{manifestPath} safeSample path does not exist: {safeSamplePath}");
            }
        }

        return safeSamplePaths;
    }
}

internal sealed record SampleExpectation(string DiagnosticPath, IReadOnlyList<string> SamplePaths, IReadOnlyList<string> ExpectedRuleIds);

internal sealed record SampleExpectationGroup(string DiagnosticPath, IReadOnlyList<string> SamplePaths, IReadOnlyList<string> ExpectedRuleIds);
