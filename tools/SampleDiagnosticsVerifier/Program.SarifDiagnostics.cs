using System.Text.Json;
using System.Text.RegularExpressions;

internal partial class Program
{
    private static IReadOnlyList<SampleDiagnostic> ParseDiagnostics(string errorLogPath, string sampleProjectDirectory)
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
                if (IsSuppressed(result))
                {
                    continue;
                }

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

    private static bool IsSuppressed(JsonElement result)
    {
        if (result.TryGetProperty("suppressions", out var suppressions) &&
            suppressions.ValueKind == JsonValueKind.Array &&
            suppressions.GetArrayLength() > 0)
        {
            return true;
        }

        if (!result.TryGetProperty("suppressionStates", out var suppressionStates) ||
            suppressionStates.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return suppressionStates.EnumerateArray()
            .Select(state => state.GetString())
            .Any(state => string.Equals(state, "suppressedInSource", StringComparison.Ordinal) ||
                          string.Equals(state, "suppressedExternally", StringComparison.Ordinal));
    }

    private static bool TryGetDiagnosticPath(JsonElement result, out string path)
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

    private static bool TryNormalizeUriPath(JsonElement uriProperty, out string path)
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

    private static string NormalizeRelativePath(string sampleProjectDirectory, string path)
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
}

internal sealed record SampleDiagnostic(string RelativePath, string Id);
