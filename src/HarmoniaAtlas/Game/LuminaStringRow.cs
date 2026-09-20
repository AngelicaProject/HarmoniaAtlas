namespace HarmoniaAtlas.Game;

public sealed record LuminaStringRow(
    uint RowId,
    ushort SubrowId,
    IReadOnlyList<LuminaStringValue> Values);

public sealed record LuminaStringValue(
    int ColumnIndex,
    string MacroText);
