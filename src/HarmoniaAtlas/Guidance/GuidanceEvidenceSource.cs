using HarmoniaAtlas.Game;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Guidance;

public sealed record GuidanceSheetMetadata(
    string Name,
    HarmoniaSheetVariant Variant,
    string EffectiveLanguage,
    IReadOnlyList<HarmoniaColumnDefinition> Columns,
    byte[] SchemaHash,
    bool LanguageSafe = true);

public sealed record GuidanceStringRow(
    uint RowId,
    ushort SubrowId,
    IReadOnlyList<GuidanceStringValue> Values);

public sealed record GuidanceStringValue(
    int ColumnIndex,
    string MacroText);

public sealed record GuidanceScanProgress(
    string Language,
    string Sheet,
    int SheetIndex,
    int SheetCount,
    long RowsProcessed);

public interface IGuidanceEvidenceSource : IDisposable
{
    string Language { get; }
    string GameVersion { get; }
    string Scope { get; }
    string? ContentId { get; }
    string? SnapshotId { get; }
    IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets { get; }
    IEnumerable<GuidanceStringRow> ReadStringRows(string sheetName);
}

public sealed class HxsGuidanceEvidenceSource : IGuidanceEvidenceSource
{
    private readonly HxsReader _reader;

    private HxsGuidanceEvidenceSource(HxsReader reader, HxsMetadata metadata, IReadOnlyDictionary<string, GuidanceSheetMetadata> sheets)
    {
        _reader = reader;
        Metadata = metadata;
        Sheets = sheets;
    }

    public HxsMetadata Metadata { get; }
    public string Language => Metadata.Language;
    public string GameVersion => Metadata.GameVersion;
    public string Scope => Metadata.Scope;
    public string? ContentId => Metadata.ContentId;
    public string? SnapshotId => Metadata.SnapshotId;
    public IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets { get; }

    public static HxsGuidanceEvidenceSource OpenVerified(string path)
    {
        try
        {
            HxsVerificationResult verification = HxsVerifier.Verify(path);
            HxsReader reader = HxsReader.OpenReadOnly(path);
            try
            {
                IReadOnlyDictionary<string, GuidanceSheetMetadata> sheets = reader.ReadSheets()
                    .ToDictionary(
                        sheet => sheet.Name,
                        sheet => new GuidanceSheetMetadata(
                            sheet.Name,
                            sheet.Variant,
                            sheet.EffectiveLanguage,
                            sheet.Columns,
                            sheet.SchemaHash),
                        StringComparer.Ordinal);
                return new HxsGuidanceEvidenceSource(reader, verification.Metadata, sheets);
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }
        catch (SourceGuidanceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SourceGuidanceException($"HXS input '{path}' failed full verification: {exception.Message}", exception);
        }
    }

    public IEnumerable<GuidanceStringRow> ReadStringRows(string sheetName)
    {
        foreach (HxsStringRowRecord row in _reader.ReadStringRows(sheetName))
        {
            yield return new GuidanceStringRow(
                row.RowId,
                row.SubrowId,
                row.Values.Select(value => new GuidanceStringValue(value.ColumnIndex, value.MacroText)).ToArray());
        }
    }

    public void Dispose() => _reader.Dispose();
}

public sealed class LuminaGuidanceEvidenceSource : IGuidanceEvidenceSource
{
    private readonly LuminaSource _source;
    private readonly IReadOnlyDictionary<string, LuminaSheet> _luminaSheets;

    private LuminaGuidanceEvidenceSource(
        LuminaSource source,
        string gameVersion,
        IReadOnlyDictionary<string, GuidanceSheetMetadata> sheets,
        IReadOnlyDictionary<string, LuminaSheet> luminaSheets,
        string language)
    {
        _source = source;
        _luminaSheets = luminaSheets;
        GameVersion = gameVersion;
        Sheets = sheets;
        Language = language;
    }

    public string Language { get; }
    public string GameVersion { get; }
    public string Scope => "full";
    public string? ContentId => null;
    public string? SnapshotId => null;
    public IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets { get; }

    public static LuminaGuidanceEvidenceSource Open(GameInstallation installation, GameLanguage language)
    {
        ArgumentNullException.ThrowIfNull(installation);
        string languageCode = language.ToCode();
        LuminaSource source = LuminaSource.Open(installation, language);
        try
        {
            string gameVersion = new GameVersionReader().Read(installation);
            Dictionary<string, LuminaSheet> luminaSheets = new(StringComparer.Ordinal);
            Dictionary<string, GuidanceSheetMetadata> sheets = new(StringComparer.Ordinal);
            string[] names = source.SheetNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            foreach (string sheetName in names)
            {
                LuminaSheet sheet = source.OpenSheet(sheetName);
                HarmoniaSheetInfo info = sheet.Info;
                byte[] schemaHash = HxsHashing.HashSchema(info.Name, info.Variant, info.Columns);
                bool languageSafe = string.Equals(info.EffectiveLanguage, "none", StringComparison.Ordinal) ||
                                    string.Equals(info.EffectiveLanguage, languageCode, StringComparison.Ordinal);
                luminaSheets.Add(sheetName, sheet);
                sheets.Add(sheetName, new GuidanceSheetMetadata(
                    info.Name,
                    info.Variant,
                    info.EffectiveLanguage,
                    info.Columns,
                    schemaHash,
                    languageSafe));
            }

            return new LuminaGuidanceEvidenceSource(source, gameVersion, sheets, luminaSheets, languageCode);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public IEnumerable<GuidanceStringRow> ReadStringRows(string sheetName)
    {
        GuidanceSheetMetadata metadata = Sheets[sheetName];
        if (!metadata.LanguageSafe)
        {
            yield break;
        }

        foreach (LuminaStringRow row in _luminaSheets[sheetName].EnumerateStringRows())
        {
            yield return new GuidanceStringRow(
                row.RowId,
                row.SubrowId,
                row.Values.Select(value => new GuidanceStringValue(value.ColumnIndex, value.MacroText)).ToArray());
        }
    }

    public void Dispose() => _source.Dispose();
}
