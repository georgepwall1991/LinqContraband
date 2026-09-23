namespace LinqContraband.Scan;

/// <summary>Command-line options for <c>linqcontraband-scan</c>.</summary>
internal sealed record ScanOptions
{
    public const string DefaultSarifPath = "linqcontraband.sarif";

    /// <summary>A solution, project or directory to build. Defaults to the current directory.</summary>
    public string Target { get; init; } = ".";

    public string SarifPath { get; init; } = DefaultSarifPath;

    public string? Configuration { get; init; }

    public string? Framework { get; init; }

    public bool NoRestore { get; init; }

    /// <summary>How many files to list under "Most affected files".</summary>
    public int Top { get; init; } = 10;

    public bool Verbose { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public const string Usage =
        """
        Usage: linqcontraband-scan [<path>] [options]

        Builds a solution or project with the LinqContraband EF Core analyzers injected and reports
        what they find. Nothing in your repository is changed; the build writes its usual bin/ and obj/.

        Arguments:
          <path>                     Solution (.sln/.slnx), project, or directory. Default: current directory.

        Options:
          -o, --sarif <file>         Where to write the SARIF 2.1.0 report. Default: linqcontraband.sarif
          -c, --configuration <cfg>  Build configuration (passed to dotnet build).
          -f, --framework <tfm>      Target framework to build (passed to dotnet build).
              --no-restore           Skip the implicit restore.
              --top <n>              Number of files to list under "Most affected files". Default: 10
          -v, --verbose              Show the full dotnet build output.
              --version              Show the scanner and analyzer version.
          -h, --help                 Show this help.
        """;

    /// <summary>Parses arguments, or returns an error message for the first bad one.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out ScanOptions options, out string? error)
    {
        options = new ScanOptions();
        error = null;
        string? target = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "-?":
                    options = options with { ShowHelp = true };
                    break;
                case "--version":
                    options = options with { ShowVersion = true };
                    break;
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "--no-restore":
                    options = options with { NoRestore = true };
                    break;
                case "-o" or "--sarif":
                    if (!TryTakeValue(args, ref i, out var sarif, out error))
                        return false;
                    options = options with { SarifPath = sarif };
                    break;
                case "-c" or "--configuration":
                    if (!TryTakeValue(args, ref i, out var configuration, out error))
                        return false;
                    options = options with { Configuration = configuration };
                    break;
                case "-f" or "--framework":
                    if (!TryTakeValue(args, ref i, out var framework, out error))
                        return false;
                    options = options with { Framework = framework };
                    break;
                case "--top":
                    if (!TryTakeValue(args, ref i, out var topText, out error))
                        return false;
                    if (!int.TryParse(topText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var top))
                    {
                        error = $"--top expects a whole number, got '{topText}'.";
                        return false;
                    }
                    options = options with { Top = top };
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option '{arg}'.";
                        return false;
                    }
                    if (target is not null)
                    {
                        error = $"Only one path can be scanned at a time; got '{target}' and '{arg}'.";
                        return false;
                    }
                    target = arg;
                    break;
            }
        }

        if (target is not null)
            options = options with { Target = target };

        return true;
    }

    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string value, out string? error)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            error = $"{args[index]} needs a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }
}
