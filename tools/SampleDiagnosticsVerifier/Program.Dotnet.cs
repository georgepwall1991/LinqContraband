using System.Diagnostics;

internal partial class Program
{
    private static string RunDotnetBuild(string sampleProjectPath, string sampleProjectDirectory, string framework, string configuration, string errorLogPath)
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

    private static void RunDotnetRestore(string sampleProjectPath, string sampleProjectDirectory)
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
        {
            return;
        }

        var combinedOutput = stdout;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            combinedOutput += Environment.NewLine + stderr;
        }

        Console.Error.WriteLine(combinedOutput);
        throw new InvalidOperationException($"dotnet restore failed with exit code {process.ExitCode}.");
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }
}
