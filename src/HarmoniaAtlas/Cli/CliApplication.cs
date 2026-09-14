using System.Text.Json;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Cli;

public static class CliApplication
{
    public static int Execute(CliParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        CliOptions options = parseResult.Options ?? new CliOptions();
        return parseResult.Command switch
        {
            CliCommand.Extract => RunExtract(options),
            CliCommand.Verify => RunVerify(options),
            CliCommand.Inspect => RunInspect(options),
            _ => throw new InvalidOperationException("No executable CLI command was selected."),
        };
    }

    private static int RunExtract(CliOptions options)
    {
        ExtractionSummary summary = new ExtractionEngine().Extract(
            options.GamePath!,
            options.Language!,
            options.OutputPath!);
        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
        }
        else
        {
            Console.WriteLine($"Game version: {summary.GameVersion}");
            Console.WriteLine($"Language: {summary.Language}");
            Console.WriteLine($"SnapshotId: {summary.SnapshotId}");
            Console.WriteLine($"ContentId: {summary.ContentId}");
            Console.WriteLine($"Sheet count: {summary.SheetCount}");
            Console.WriteLine($"Row count: {summary.RowCount}");
            Console.WriteLine($"String count: {summary.StringCount}");
            Console.WriteLine($"Output path: {summary.OutputPath}");
        }

        return 0;
    }

    private static int RunVerify(CliOptions options)
    {
        HxsVerifier.Verify(options.HxsPath!);
        Console.WriteLine("valid");
        return 0;
    }

    private static int RunInspect(CliOptions options)
    {
        HxsInspection inspection = HxsInspector.Inspect(options.HxsPath!);
        Console.WriteLine(options.Json ? inspection.ToJson() : inspection.ToText());
        return 0;
    }
}
