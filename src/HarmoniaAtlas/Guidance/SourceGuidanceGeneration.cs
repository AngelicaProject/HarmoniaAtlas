namespace HarmoniaAtlas.Guidance;

public sealed class SourceGuidanceGenerator
{
    public SourceGuidanceSummary Generate(IReadOnlyList<string> inputPaths, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(inputPaths);
        SourceGuidanceWriter.Write(bundle, outputPath);
        return Summarize(bundle, Path.GetFullPath(outputPath));
    }

    public static SourceGuidanceSummary Summarize(SourceGuidanceBundle bundle, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return new SourceGuidanceSummary(
            bundle.GameVersion,
            bundle.Scope,
            bundle.Inputs.Select(input => input.Language).ToArray(),
            bundle.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Compatible),
            bundle.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Incompatible),
            checked(bundle.Sheets.Sum(sheet => sheet.Translatable.Count)),
            bundle.BundleId,
            Path.GetFullPath(outputPath));
    }
}
