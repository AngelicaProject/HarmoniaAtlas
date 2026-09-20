namespace HarmoniaAtlas.Guidance;

public sealed class SourceGuidanceGenerator
{
    public SourceGuidanceSummary Generate(string sourcePath, IReadOnlyList<string> comparePaths, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(comparePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(sourcePath, comparePaths);
        SourceGuidanceWriter.Write(bundle, outputPath);
        return Summarize(bundle, Path.GetFullPath(outputPath));
    }

    public SourceGuidanceSummary Generate(
        IGuidanceEvidenceSource source,
        IReadOnlyList<IGuidanceEvidenceSource> compareInputs,
        string outputPath,
        Action<GuidanceScanProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compareInputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(source, compareInputs, progress);
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
            bundle.EvidenceInputs.Select(input => input.Language).ToArray(),
            bundle.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Compatible),
            bundle.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Incompatible),
            checked(bundle.Sheets.Sum(sheet => sheet.Translatable.Count)),
            bundle.BundleId,
            Path.GetFullPath(outputPath));
    }
}
