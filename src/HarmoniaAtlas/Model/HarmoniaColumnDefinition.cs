namespace HarmoniaAtlas.Model;

public sealed record HarmoniaColumnDefinition(int Index, int Offset, HarmoniaColumnType Type);

public sealed record HarmoniaSheetInfo(
    string Name,
    HarmoniaSheetVariant Variant,
    string EffectiveLanguage,
    IReadOnlyList<HarmoniaColumnDefinition> Columns);

public sealed record HarmoniaCellData(
    int ColumnIndex,
    HarmoniaColumnType Type,
    object? TechnicalValue,
    string? MacroText,
    byte[]? RawValue)
{
    public static HarmoniaCellData Technical(int columnIndex, HarmoniaColumnType type, object value) =>
        new(columnIndex, type, value, null, null);

    public static HarmoniaCellData String(int columnIndex, string macroText, byte[]? rawValue) =>
        new(columnIndex, HarmoniaColumnType.String, null, macroText, rawValue);
}

public sealed record HarmoniaRowData(
    uint RowId,
    ushort SubrowId,
    IReadOnlyList<HarmoniaCellData> Cells);
