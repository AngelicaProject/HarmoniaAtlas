using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Game;

/// <summary>
/// The game sheet catalog read by extraction. <see cref="LuminaSource"/> is the
/// production implementation.
/// </summary>
public interface IExtractionSource
{
    IReadOnlyList<string> SheetNames { get; }

    /// <summary>
    /// Opens one sheet or raises <see cref="SheetReadException"/> when the sheet
    /// cannot be represented or read.
    /// </summary>
    IExtractionSheet OpenSheet(string sheetName);
}

public interface IExtractionSheet
{
    HarmoniaSheetInfo Info { get; }

    IEnumerable<HarmoniaRowData> EnumerateRows();
}
