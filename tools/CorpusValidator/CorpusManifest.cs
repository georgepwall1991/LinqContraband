using System.Text.Json;

namespace LinqContraband.CorpusValidator;

public sealed record CorpusManifestEntry(string Name, string Repository, string Commit, string[] Projects)
{
    public string ProjectPath(string project) => Path.Combine(Name, project);
}

public sealed record CorpusManifest(int Version, IReadOnlyList<CorpusManifestEntry> Corpus)
{
    public static CorpusManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Corpus manifest not found: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (!root.TryGetProperty("version", out var versionElement) || versionElement.GetInt32() != 1)
        {
            throw new InvalidOperationException($"Corpus manifest at {path} must declare version 1.");
        }

        if (!root.TryGetProperty("corpus", out var corpusElement) || corpusElement.ValueKind != JsonValueKind.Array || corpusElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Corpus manifest at {path} must declare a non-empty corpus array.");
        }

        var entries = new List<CorpusManifestEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in corpusElement.EnumerateArray())
        {
            string ReadRequiredString(string property)
            {
                if (!entry.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                {
                    throw new InvalidOperationException($"Corpus manifest entry at {path} is missing required string property '{property}'.");
                }

                return value.GetString()!;
            }

            var name = ReadRequiredString("name");
            var repository = ReadRequiredString("repository");
            var commit = ReadRequiredString("commit");

            if (!names.Add(name))
            {
                throw new InvalidOperationException($"Corpus manifest at {path} declares duplicate corpus name '{name}'.");
            }

            if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"Corpus manifest entry '{name}' must be a single safe directory segment used as the checkout folder name.");
            }

            if (!repository.EndsWith(".git", StringComparison.Ordinal) ||
                !repository.StartsWith("https://", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Corpus manifest entry '{name}' must declare an https .git repository URL.");
            }

            if (commit.Length != 40 || commit.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            {
                throw new InvalidOperationException($"Corpus manifest entry '{name}' must pin a full 40-character lower-case commit hash.");
            }

            if (!entry.TryGetProperty("projects", out var projectsElement) ||
                projectsElement.ValueKind != JsonValueKind.Array ||
                projectsElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"Corpus manifest entry '{name}' must declare a non-empty projects array.");
            }

            var projects = new List<string>();
            foreach (var project in projectsElement.EnumerateArray())
            {
                var projectPath = project.GetString();
                if (string.IsNullOrWhiteSpace(projectPath) || !projectPath.EndsWith(".csproj", StringComparison.Ordinal) || Path.IsPathRooted(projectPath) || projectPath.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Corpus manifest entry '{name}' declares invalid project path '{projectPath}'. Expected a repo-relative .csproj path.");
                }

                projects.Add(NormalizePath(projectPath));
            }

            entries.Add(new CorpusManifestEntry(name, repository, commit, projects.ToArray()));
        }

        return new CorpusManifest(1, entries);
    }

    public static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
