using System.Text.Json;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Cli;

public static class CliApplication
{
    public static int Execute(CliParseResult parseResult)
        => Execute(parseResult, Console.Out);

    public static int Execute(CliParseResult parseResult, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(output);
        CliOptions options = parseResult.Options ?? new CliOptions();
        return parseResult.Command switch
        {
            CliCommand.Version => RunVersion(output),
            CliCommand.Extract => RunExtract(options, output),
            CliCommand.Verify => RunVerify(options, output),
            CliCommand.Inspect => RunInspect(options, output),
            CliCommand.Guidance => RunGuidance(options, output),
            _ => throw new InvalidOperationException("No executable CLI command was selected."),
        };
    }

    private static int RunVersion(TextWriter output)
    {
        output.WriteLine(AtlasApplicationVersion.Current);
        return 0;
    }

    private static int RunExtract(CliOptions options, TextWriter output)
    {
        ExtractionSummary summary = new ExtractionEngine().Extract(
            options.GamePath!,
            options.Language!,
            options.OutputPath!);
        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
        }
        else
        {
            output.WriteLine($"Game version: {summary.GameVersion}");
            output.WriteLine($"Language: {summary.Language}");
            output.WriteLine($"SnapshotId: {summary.SnapshotId}");
            output.WriteLine($"ContentId: {summary.ContentId}");
            output.WriteLine($"Sheet count: {summary.SheetCount}");
            output.WriteLine($"Row count: {summary.RowCount}");
            output.WriteLine($"String count: {summary.StringCount}");
            output.WriteLine($"Output path: {summary.OutputPath}");
        }

        return 0;
    }

    private static int RunVerify(CliOptions options, TextWriter output)
    {
        HxsVerifier.Verify(options.HxsPath!);
        output.WriteLine("valid");
        return 0;
    }

    private static int RunInspect(CliOptions options, TextWriter output)
    {
        HxsInspection inspection = HxsInspector.Inspect(options.HxsPath!);
        output.WriteLine(options.Json ? inspection.ToJson() : inspection.ToText());
        return 0;
    }

    private static int RunGuidance(CliOptions options, TextWriter output)
    {
        SourceGuidanceSummary summary = new SourceGuidanceGenerator().Generate(options.InputPaths!, options.OutputPath!);
        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
        }
        else
        {
            output.WriteLine($"Game version: {summary.GameVersion}");
            output.WriteLine($"Scope: {summary.Scope}");
            output.WriteLine($"Languages: {string.Join(", ", summary.Languages)}");
            output.WriteLine($"Compatible sheet count: {summary.CompatibleSheetCount}");
            output.WriteLine($"Incompatible sheet count: {summary.IncompatibleSheetCount}");
            output.WriteLine($"Translatable column count: {summary.TranslatableColumnCount}");
            output.WriteLine($"Context column count: {summary.ContextColumnCount}");
            output.WriteLine($"Unknown column count: {summary.UnknownColumnCount}");
            output.WriteLine($"BundleId: {summary.BundleId}");
            output.WriteLine($"Output path: {summary.OutputPath}");
        }

        return 0;
    }
}
