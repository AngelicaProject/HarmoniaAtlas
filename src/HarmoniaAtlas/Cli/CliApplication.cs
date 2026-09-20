using System.Text.Json;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Package;

namespace HarmoniaAtlas.Cli;

public static class CliApplication
{
    public static int Execute(CliParseResult parseResult)
        => Execute(parseResult, Console.Out, Console.Error);

    public static int Execute(CliParseResult parseResult, TextWriter output)
        => Execute(parseResult, output, Console.Error);

    public static int Execute(CliParseResult parseResult, TextWriter output, TextWriter diagnostics)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(diagnostics);
        CliOptions options = parseResult.Options ?? new CliOptions();
        return parseResult.Command switch
        {
            CliCommand.Version => RunVersion(output),
            CliCommand.Extract => RunExtract(options, output),
            CliCommand.Verify => RunVerify(options, output),
            CliCommand.Inspect => RunInspect(options, output),
            CliCommand.Guidance => RunGuidance(options, output),
            CliCommand.Package => RunPackage(options, output, diagnostics),
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
        SourceGuidanceSummary summary = new SourceGuidanceGenerator().Generate(
            options.SourcePath!,
            options.ComparePaths!,
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
            output.WriteLine($"Scope: {summary.Scope}");
            output.WriteLine($"Languages: {string.Join(", ", summary.Languages)}");
            output.WriteLine($"Compatible sheet count: {summary.CompatibleSheetCount}");
            output.WriteLine($"Incompatible sheet count: {summary.IncompatibleSheetCount}");
            output.WriteLine($"Translatable occurrence count: {summary.TranslatableOccurrenceCount}");
            output.WriteLine($"BundleId: {summary.BundleId}");
            output.WriteLine($"Output path: {summary.OutputPath}");
        }

        return 0;
    }

    private static int RunPackage(CliOptions options, TextWriter output, TextWriter diagnostics)
    {
        try
        {
            PackageResult result = new PackagePipeline().Generate(
                options.GamePath!,
                options.Language!,
                options.OutputPath!,
                options.EventsJsonl ? packageEvent => WritePackageEvent(output, packageEvent) : null);
            if (options.EventsJsonl)
            {
                return 0;
            }

            output.WriteLine($"Game version: {result.Package.SourceMetadata.GameVersion}");
            output.WriteLine($"Source language: {result.Package.SourceMetadata.Language}");
            output.WriteLine($"Evidence languages: {string.Join(", ", result.EvidenceLanguages)}");
            output.WriteLine($"SnapshotId: {result.Package.SourceMetadata.SnapshotId}");
            output.WriteLine($"ContentId: {result.Package.SourceMetadata.ContentId}");
            output.WriteLine($"Translatable occurrence count: {result.TranslatableOccurrenceCount}");
            output.WriteLine($"PackageId: {result.Package.Manifest.PackageId}");
            output.WriteLine($"Output path: {result.Package.OutputPath}");
            output.WriteLine($"Inspect installation: {result.InspectInstallationMs} ms");
            output.WriteLine($"Extract source: {result.ExtractSourceMs} ms");
            output.WriteLine($"Verify source: {result.VerifySourceMs} ms");
            output.WriteLine($"Scan evidence: {result.ScanEvidenceMs} ms");
            output.WriteLine($"Write package: {result.WritePackageMs} ms");
            output.WriteLine($"Validate package: {result.ValidatePackageMs} ms");
            output.WriteLine($"Elapsed: {result.ElapsedMs} ms");
            return 0;
        }
        catch (Exception exception)
        {
            if (options.EventsJsonl)
            {
                WritePackageEvent(output, new PackageEvent("failed", new Dictionary<string, object?>
                {
                    ["protocolVersion"] = 1,
                    ["code"] = exception.GetType().Name,
                    ["message"] = exception.Message,
                }));
            }

            diagnostics.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static void WritePackageEvent(TextWriter output, PackageEvent packageEvent)
    {
        Dictionary<string, object?> values = packageEvent.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values["type"] = packageEvent.Type;
        output.WriteLine(JsonSerializer.Serialize(values));
    }
}
