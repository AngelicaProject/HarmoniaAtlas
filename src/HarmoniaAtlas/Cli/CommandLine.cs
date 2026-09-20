namespace HarmoniaAtlas.Cli;

public enum CliCommand
{
    Help,
    Version,
    Extract,
    Verify,
    Inspect,
    Guidance,
    Package,
}

public sealed record CliOptions(
    string? GamePath = null,
    string? Language = null,
    string? OutputPath = null,
    string? HxsPath = null,
    bool Json = false,
    bool EventsJsonl = false,
    string? SourcePath = null,
    IReadOnlyList<string>? ComparePaths = null);

public sealed record CliParseResult(CliCommand? Command, CliOptions? Options, string? Error)
{
    public static CliParseResult Success(CliCommand command, CliOptions options) =>
        new(command, options, null);

    public static CliParseResult Failure(string error) =>
        new(null, null, error);
}

public static class CliUsage
{
    public static readonly string Text = "Usage: harmonia-atlas <extract|verify|inspect|guidance|package> [options]" + Environment.NewLine +
                                         "  harmonia-atlas --version" + Environment.NewLine +
                                         "  extract --game-path <path> --language <language> --output <path> [--json]" + Environment.NewLine +
                                         "  verify <path.hxs>" + Environment.NewLine +
                                         "  inspect <path.hxs> [--json]" + Environment.NewLine +
                                         "  guidance --source <path.hxs> --compare <path.hxs> --output <path.hsg.json> [--json]" + Environment.NewLine +
                                         "  package --game-path <path> --language <language> --output <path.hsp> [--events jsonl]";
}

public static class CommandLineParser
{
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || IsHelp(args[0]))
        {
            return CliParseResult.Success(CliCommand.Help, new CliOptions());
        }

        if (args.Count == 1 && IsVersion(args[0]))
        {
            return CliParseResult.Success(CliCommand.Version, new CliOptions());
        }

        return args[0] switch
        {
            "extract" => ParseExtract(args),
            "verify" => ParseSinglePathCommand(args, CliCommand.Verify),
            "inspect" => ParseInspect(args),
            "guidance" => ParseGuidance(args),
            "package" => ParsePackage(args),
            _ => CliParseResult.Failure($"unknown command '{args[0]}'"),
        };
    }

    private static CliParseResult ParseExtract(IReadOnlyList<string> args)
    {
        string? gamePath = null;
        string? language = null;
        string? outputPath = null;
        bool json = false;

        for (int index = 1; index < args.Count; index++)
        {
            string option = args[index];
            if (!TryReadValue(args, ref index, option, out string? value, out string? error))
            {
                return CliParseResult.Failure(error!);
            }

            switch (option)
            {
                case "--json":
                    if (json)
                    {
                        return CliParseResult.Failure("--json was specified more than once");
                    }

                    json = true;
                    break;
                case "--game-path":
                    if (gamePath is not null)
                    {
                        return CliParseResult.Failure("--game-path was specified more than once");
                    }

                    gamePath = value;
                    break;
                case "--language":
                    if (language is not null)
                    {
                        return CliParseResult.Failure("--language was specified more than once");
                    }

                    language = value;
                    break;
                case "--output":
                    if (outputPath is not null)
                    {
                        return CliParseResult.Failure("--output was specified more than once");
                    }

                    outputPath = value;
                    break;
                default:
                    return CliParseResult.Failure($"unknown extract option '{option}'");
            }
        }

        if (gamePath is null || language is null || outputPath is null)
        {
            return CliParseResult.Failure("extract requires --game-path, --language, and --output");
        }

        return CliParseResult.Success(
            CliCommand.Extract,
            new CliOptions(GamePath: gamePath, Language: language, OutputPath: outputPath, Json: json));
    }

    private static CliParseResult ParseSinglePathCommand(IReadOnlyList<string> args, CliCommand command)
    {
        if (args.Count != 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("-", StringComparison.Ordinal))
        {
            return CliParseResult.Failure($"{command.ToString().ToLowerInvariant()} requires one .hxs path");
        }

        return CliParseResult.Success(command, new CliOptions(HxsPath: args[1]));
    }

    private static CliParseResult ParseInspect(IReadOnlyList<string> args)
    {
        string? path = null;
        bool json = false;

        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--json")
            {
                if (json)
                {
                    return CliParseResult.Failure("--json was specified more than once");
                }

                json = true;
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                return CliParseResult.Failure($"unknown inspect option '{argument}'");
            }

            if (path is not null)
            {
                return CliParseResult.Failure("inspect requires one .hxs path");
            }

            path = argument;
        }

        return path is null
            ? CliParseResult.Failure("inspect requires one .hxs path")
             : CliParseResult.Success(CliCommand.Inspect, new CliOptions(HxsPath: path, Json: json));
    }

    private static CliParseResult ParseGuidance(IReadOnlyList<string> args)
    {
        string? sourcePath = null;
        List<string> comparePaths = new();
        HashSet<string> seenPaths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? outputPath = null;
        bool json = false;

        for (int index = 1; index < args.Count; index++)
        {
            string option = args[index];
            if (!TryReadValue(args, ref index, option, out string? value, out string? error))
            {
                return CliParseResult.Failure(error!);
            }

            switch (option)
            {
                case "--source":
                    if (sourcePath is not null)
                    {
                        return CliParseResult.Failure("--source was specified more than once");
                    }

                    sourcePath = value;
                    if (!seenPaths.Add(Path.GetFullPath(value!)))
                    {
                        return CliParseResult.Failure("guidance source and comparison paths must not be duplicated");
                    }
                    break;
                case "--compare":
                    if (!seenPaths.Add(Path.GetFullPath(value!)))
                    {
                        return CliParseResult.Failure("guidance source and comparison paths must not be duplicated");
                    }

                    comparePaths.Add(value!);
                    break;
                case "--output":
                    if (outputPath is not null)
                    {
                        return CliParseResult.Failure("--output was specified more than once");
                    }

                    outputPath = value;
                    break;
                case "--json":
                    if (json)
                    {
                        return CliParseResult.Failure("--json was specified more than once");
                    }

                    json = true;
                    break;
                default:
                    return CliParseResult.Failure($"unknown guidance option '{option}'");
            }
        }

        if (sourcePath is null || comparePaths.Count == 0 || outputPath is null)
        {
            return CliParseResult.Failure("guidance requires one --source path, at least one --compare path, and --output");
        }

        return CliParseResult.Success(
            CliCommand.Guidance,
            new CliOptions(OutputPath: outputPath, Json: json, SourcePath: sourcePath, ComparePaths: comparePaths));
    }

    private static CliParseResult ParsePackage(IReadOnlyList<string> args)
    {
        string? gamePath = null;
        string? language = null;
        string? outputPath = null;
        bool eventsJsonl = false;

        for (int index = 1; index < args.Count; index++)
        {
            string option = args[index];
            if (option == "--events")
            {
                if (index + 1 >= args.Count || !string.Equals(args[++index], "jsonl", StringComparison.Ordinal))
                {
                    return CliParseResult.Failure("--events supports only 'jsonl'");
                }

                if (eventsJsonl)
                {
                    return CliParseResult.Failure("--events was specified more than once");
                }

                eventsJsonl = true;
                continue;
            }

            if (!TryReadValue(args, ref index, option, out string? value, out string? error))
            {
                return CliParseResult.Failure(error!);
            }

            switch (option)
            {
                case "--game-path":
                    if (gamePath is not null)
                    {
                        return CliParseResult.Failure("--game-path was specified more than once");
                    }

                    gamePath = value;
                    break;
                case "--language":
                    if (language is not null)
                    {
                        return CliParseResult.Failure("--language was specified more than once");
                    }

                    language = value;
                    break;
                case "--output":
                    if (outputPath is not null)
                    {
                        return CliParseResult.Failure("--output was specified more than once");
                    }

                    outputPath = value;
                    break;
                default:
                    return CliParseResult.Failure($"unknown package option '{option}'");
            }
        }

        if (gamePath is null || language is null || outputPath is null)
        {
            return CliParseResult.Failure("package requires --game-path, --language, and --output");
        }

        if (!outputPath.EndsWith(".hsp", StringComparison.OrdinalIgnoreCase))
        {
            return CliParseResult.Failure("package --output must use the .hsp extension");
        }

        return CliParseResult.Success(
            CliCommand.Package,
            new CliOptions(GamePath: gamePath, Language: language, OutputPath: outputPath, EventsJsonl: eventsJsonl));
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;

        if (option is not ("--game-path" or "--language" or "--output" or "--source" or "--compare"))
        {
            return true;
        }

        if (index + 1 >= args.Count)
        {
            error = $"{option} requires a value";
            return false;
        }

        value = args[++index];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
        {
            error = $"{option} requires a value";
            return false;
        }

        return true;
    }

    private static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    private static bool IsVersion(string value) => value is "--version" or "version";
}
