using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Hxs;

public sealed record HxsMetadata(
    int FormatVersion,
    string GameVersion,
    string Language,
    string Scope,
    string ContentId,
    string SnapshotId,
    string ExtractorVersion,
    string LuminaVersion,
    long SheetCount,
    long RowCount,
    long StringCellCount,
    long ExcludedSheetCount);

/// <summary>
/// Why a sheet in the game catalog is not stored in an HXS. Codes are persisted.
/// </summary>
public enum HxsSheetExclusionReason
{
    UnsupportedVariant = 1,
    UnsupportedColumnType = 2,
    UnreadableData = 3,
}

public sealed record HxsExcludedSheet(string Name, HxsSheetExclusionReason Reason);

public sealed record HxsStringCellRecord(
    uint RowId,
    ushort SubrowId,
    int ColumnIndex,
    string MacroText,
    byte[]? RawValue,
    byte[] MacroHash,
    byte[]? RawHash);

public sealed record HxsTechnicalCell(int ColumnIndex, HarmoniaColumnType Type, byte[] CanonicalValue);

public sealed record HxsRowRecord(
    uint RowId,
    ushort SubrowId,
    byte[] TechnicalPayload,
    byte[] RowHash,
    byte[] TechnicalHash,
    byte[] StringHash,
    IReadOnlyList<HxsStringCellRecord> StringCells);

public sealed record HxsStringOccurrenceValue(
    uint RowId,
    ushort SubrowId,
    int ColumnIndex,
    string MacroText);

public sealed record HxsStringRowRecord(
    uint RowId,
    ushort SubrowId,
    IReadOnlyList<HxsStringOccurrenceValue> Values);

public sealed record HxsSheetRecord(
    string Name,
    HarmoniaSheetVariant Variant,
    string EffectiveLanguage,
    IReadOnlyList<HarmoniaColumnDefinition> Columns,
    int RowCount,
    byte[] SchemaHash,
    byte[] TechnicalHash,
    byte[] StringHash,
    byte[] ContentHash);

public sealed record HxsSnapshotSummary(
    HxsMetadata Metadata,
    IReadOnlyList<HxsSheetRecord> Sheets,
    IReadOnlyList<HxsExcludedSheet> ExcludedSheets);
