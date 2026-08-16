using System.Diagnostics;

namespace LinqContraband.CorpusValidator;

public static class GitCorpus
{
    public static void EnsurePinned(CorpusManifestEntry entry, string corpusRoot)
    {
        var checkoutPath = Path.Combine(corpusRoot, entry.Name);

        if (!Directory.Exists(Path.Combine(checkoutPath, ".git")))
        {
            Run(null, "clone", entry.Repository, checkoutPath);
        }

        var currentCommit = Run(checkoutPath, "rev-parse", "HEAD");
        if (string.Equals(currentCommit, entry.Commit, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[{entry.Name}] already pinned at {entry.Commit[..12]}");
            return;
        }

        Run(checkoutPath, "fetch", "origin", entry.Commit);
        Run(checkoutPath, "checkout", "--detach", entry.Commit);
        Console.WriteLine($"[{entry.Name}] pinned at {entry.Commit[..12]}");
    }

    private static string Run(string? workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = string.Join(' ', arguments),
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEnd().Trim();
        var stderr = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(stdout);
            Console.Error.WriteLine(stderr);
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }

        return stdout;
    }
}
