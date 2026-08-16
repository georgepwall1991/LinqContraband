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
        if (!string.Equals(currentCommit, entry.Commit, StringComparison.OrdinalIgnoreCase))
        {
            Run(checkoutPath, "fetch", "origin", entry.Commit);
            Run(checkoutPath, "checkout", "--detach", entry.Commit);
        }

        var status = Run(checkoutPath, "status", "--porcelain");
        if (!string.IsNullOrEmpty(status))
        {
            Console.WriteLine($"[{entry.Name}] discarding local modifications in the corpus checkout");
            Run(checkoutPath, "reset", "--hard", "HEAD");
            Run(checkoutPath, "clean", "-fd");
        }

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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(stdoutTask.Result);
            Console.Error.WriteLine(stderrTask.Result);
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }

        return stdoutTask.Result.Trim();
    }
}
