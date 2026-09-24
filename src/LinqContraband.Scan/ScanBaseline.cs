using System.Text.Json;

namespace LinqContraband.Scan;

/// <summary>
/// The findings of an earlier scan, read from the SARIF report it wrote. A finding is known when the baseline has
/// its fingerprint, which does not change when code above it moves the finding to another line. Results without a
/// fingerprint (from another tool, or edited by hand) match on rule, file and message instead.
/// </summary>
internal sealed class ScanBaseline
{
    private readonly HashSet<string> _fingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<(string RuleId, string Path, string Message)> _unfingerprinted = [];

    public int Count { get; private set; }

    public bool Contains(Finding finding, string fingerprint) =>
        _fingerprints.Contains(fingerprint) || _unfingerprinted.Contains((finding.RuleId, finding.Path, finding.Message));

    /// <summary>Reads a baseline, or throws <see cref="JsonException"/> when <paramref name="json"/> is not a SARIF log.</summary>
    public static ScanBaseline Parse(string json)
    {
        var baseline = new ScanBaseline();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            throw new JsonException("The file has no SARIF 'runs'.");

        foreach (var run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var result in results.EnumerateArray())
            {
                if (result.TryGetProperty("baselineState", out var state) && state.GetString() == "absent")
                    continue;

                baseline.Count++;
                if (result.TryGetProperty("partialFingerprints", out var fingerprints) &&
                    fingerprints.ValueKind == JsonValueKind.Object &&
                    fingerprints.TryGetProperty(ScanReport.FingerprintKey, out var fingerprint) &&
                    fingerprint.ValueKind == JsonValueKind.String)
                {
                    baseline._fingerprints.Add(fingerprint.GetString()!);
                    continue;
                }

                var ruleId = result.TryGetProperty("ruleId", out var id) ? id.GetString() : null;
                var message = result.TryGetProperty("message", out var messageElement) && messageElement.TryGetProperty("text", out var text) ? text.GetString() : null;
                var path = RelativeUri(result);
                if (ruleId is not null && message is not null && path is not null)
                    baseline._unfingerprinted.Add((ruleId, path, message));
            }
        }

        return baseline;
    }

    private static string? RelativeUri(JsonElement result)
    {
        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array ||
            locations.GetArrayLength() == 0 ||
            !locations[0].TryGetProperty("physicalLocation", out var physical) ||
            !physical.TryGetProperty("artifactLocation", out var artifact) ||
            !artifact.TryGetProperty("uri", out var uri) ||
            uri.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Uri.UnescapeDataString(uri.GetString()!);
    }
}
