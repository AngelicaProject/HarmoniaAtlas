using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Tests;

public sealed class GuidanceEvidenceSourceTests
{
    [Fact]
    public void AnalyzerEnumeratesEachEvidenceSheetOnceAndReportsCompletedScanProgress()
    {
        GuidanceSheetMetadata metadata = new(
            "Item",
            HarmoniaSheetVariant.DefaultRows,
            "none",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            new byte[32]);
        CountingEvidenceSource source = new("en", metadata, "English");
        CountingEvidenceSource comparison = new("ja", metadata, "日本語");
        List<GuidanceScanProgress> progress = new();

        SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(source, [comparison], progress.Add);

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(1, comparison.EnumerationCount);
        Assert.Equal(SourceGuidanceSheetStatus.Compatible, bundle.Sheets.Single().Status);
        Assert.Single(bundle.Sheets.Single().Translatable);
        Assert.Equal(1, progress.Count(item => item.Language == "en" && item.RowsProcessed == 1));
        Assert.Equal(1, progress.Count(item => item.Language == "ja" && item.RowsProcessed == 1));
    }

    [Fact]
    public void UnsafeSheetsAreEnumeratedOnceAndRemainInEvidenceButCannotGrantPermission()
    {
        GuidanceSheetMetadata unsafeMetadata = new(
            "Item",
            HarmoniaSheetVariant.DefaultRows,
            "en",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            new byte[32],
            LanguageSafe: false);
        GuidanceSheetMetadata safeMetadata = unsafeMetadata with { EffectiveLanguage = "none", LanguageSafe = true };
        CountingEvidenceSource source = new("ja", unsafeMetadata, "fallback");
        CountingEvidenceSource comparison = new("en", safeMetadata, "English");

        SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(source, [comparison]);

        Assert.Equal(1, source.EnumerationCount);
        Assert.Equal(1, comparison.EnumerationCount);
        Assert.Equal(SourceGuidanceSheetStatus.Incompatible, bundle.Sheets.Single().Status);
        Assert.Empty(bundle.Sheets.Single().Translatable);
    }

    private sealed class CountingEvidenceSource : IGuidanceEvidenceSource
    {
        private readonly GuidanceSheetMetadata _metadata;
        private readonly GuidanceStringRow[] _rows;

        public CountingEvidenceSource(string language, GuidanceSheetMetadata metadata, string value)
        {
            Language = language;
            _metadata = metadata;
            _rows = [new GuidanceStringRow(1, 0, [new GuidanceStringValue(0, value)])];
        }

        public string Language { get; }
        public string GameVersion => "game";
        public string Scope => "full";
        public string? ContentId => "sha256:" + new string('0', 64);
        public string? SnapshotId => "sha256:" + new string('1', 64);
        public IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets => new Dictionary<string, GuidanceSheetMetadata>(StringComparer.Ordinal)
        {
            [_metadata.Name] = _metadata,
        };
        public int EnumerationCount { get; private set; }

        public IEnumerable<GuidanceStringRow> ReadStringRows(string sheetName)
        {
            EnumerationCount++;
            foreach (GuidanceStringRow row in _rows)
            {
                yield return row;
            }
        }

        public void Dispose()
        {
        }
    }
}
