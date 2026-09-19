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
        IReadOnlyList<SourceGuidanceColumn> columns = bundle.Eligibility.Sheets.SelectMany(sheet => sheet.Columns).ToArray();
        return new SourceGuidanceSummary(
            bundle.GameVersion,
            bundle.Scope,
            bundle.Inputs.Select(input => input.Language).ToArray(),
            bundle.Eligibility.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Compatible),
            bundle.Eligibility.Sheets.Count(sheet => sheet.Status == SourceGuidanceSheetStatus.Incompatible),
            columns.Count(column => column.Role == SourceGuidanceRole.Translatable),
            columns.Count(column => column.Role == SourceGuidanceRole.Context),
            columns.Count(column => column.Role == SourceGuidanceRole.Unknown),
            bundle.BundleId,
            Path.GetFullPath(outputPath));
    }
}
