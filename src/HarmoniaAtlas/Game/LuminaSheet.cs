using Lumina.Excel;
using Lumina.Text.ReadOnly;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Game;

public sealed class LuminaSheet : IExtractionSheet
{
    private readonly IReadOnlyList<HarmoniaColumnDefinition> _columns;
    private readonly ExcelSheet<RawRow>? _defaultSheet;
    private readonly SubrowExcelSheet<RawSubrow>? _subrowSheet;

    internal LuminaSheet(
        RawExcelSheet rawSheet,
        HarmoniaSheetInfo info,
        ExcelSheet<RawRow>? defaultSheet,
        SubrowExcelSheet<RawSubrow>? subrowSheet)
    {
        if (info.Variant == HarmoniaSheetVariant.DefaultRows && defaultSheet is null ||
            info.Variant == HarmoniaSheetVariant.Subrows && subrowSheet is null)
        {
            throw new ArgumentException("The Lumina typed sheet does not match the sheet variant.", nameof(info));
        }

        Info = info;
        _columns = info.Columns;
        _defaultSheet = defaultSheet;
        _subrowSheet = subrowSheet;
        _ = rawSheet;
    }

    public HarmoniaSheetInfo Info { get; }

    public IEnumerable<HarmoniaRowData> EnumerateRows() => Info.Variant switch
    {
        HarmoniaSheetVariant.DefaultRows => EnumerateDefaultRows(),
        HarmoniaSheetVariant.Subrows => EnumerateSubrows(),
        _ => throw new NotSupportedException($"Unsupported Harmonia sheet variant: {Info.Variant}."),
    };

    public IEnumerable<LuminaStringRow> EnumerateStringRows() => Info.Variant switch
    {
        HarmoniaSheetVariant.DefaultRows => EnumerateDefaultStringRows(),
        HarmoniaSheetVariant.Subrows => EnumerateSubrowStringRows(),
        _ => throw new NotSupportedException($"Unsupported Harmonia sheet variant: {Info.Variant}."),
    };

    private IEnumerable<HarmoniaRowData> EnumerateDefaultRows()
    {
        ExcelSheet<RawRow> sheet = _defaultSheet ?? throw new InvalidOperationException("The default Lumina sheet is not initialized.");
        List<uint> rowIds = sheet.Select(row => row.RowId).ToList();
        rowIds.Sort();
        foreach (uint rowId in rowIds)
        {
            yield return ConvertRow(sheet.GetRow(rowId));
        }
    }

    private IEnumerable<HarmoniaRowData> EnumerateSubrows()
    {
        List<(uint RowId, ushort SubrowId)> coordinates = new();
        SubrowExcelSheet<RawSubrow> sheet = _subrowSheet ?? throw new InvalidOperationException("The subrow Lumina sheet is not initialized.");
        foreach (SubrowCollection<RawSubrow> collection in sheet)
        {
            foreach (RawSubrow row in collection)
            {
                coordinates.Add((row.RowId, row.SubrowId));
            }
        }

        coordinates.Sort(static (left, right) =>
        {
            int rowComparison = left.RowId.CompareTo(right.RowId);
            return rowComparison != 0 ? rowComparison : left.SubrowId.CompareTo(right.SubrowId);
        });
        foreach ((uint rowId, ushort subrowId) in coordinates)
        {
            yield return ConvertRow(sheet.GetSubrow(rowId, subrowId));
        }
    }

    private IEnumerable<LuminaStringRow> EnumerateDefaultStringRows()
    {
        ExcelSheet<RawRow> sheet = _defaultSheet ?? throw new InvalidOperationException("The default Lumina sheet is not initialized.");
        List<uint> rowIds = sheet.Select(row => row.RowId).ToList();
        rowIds.Sort();
        foreach (uint rowId in rowIds)
        {
            yield return ConvertStringRow(sheet.GetRow(rowId));
        }
    }

    private IEnumerable<LuminaStringRow> EnumerateSubrowStringRows()
    {
        List<(uint RowId, ushort SubrowId)> coordinates = new();
        SubrowExcelSheet<RawSubrow> sheet = _subrowSheet ?? throw new InvalidOperationException("The subrow Lumina sheet is not initialized.");
        foreach (SubrowCollection<RawSubrow> collection in sheet)
        {
            foreach (RawSubrow row in collection)
            {
                coordinates.Add((row.RowId, row.SubrowId));
            }
        }

        coordinates.Sort(static (left, right) =>
        {
            int rowComparison = left.RowId.CompareTo(right.RowId);
            return rowComparison != 0 ? rowComparison : left.SubrowId.CompareTo(right.SubrowId);
        });
        foreach ((uint rowId, ushort subrowId) in coordinates)
        {
            yield return ConvertStringRow(sheet.GetSubrow(rowId, subrowId));
        }
    }

    private HarmoniaRowData ConvertRow(RawRow row) =>
        new(row.RowId, 0, _columns.Select(column => ReadCell(row, column)).ToArray());

    private HarmoniaRowData ConvertRow(RawSubrow row) =>
        new(row.RowId, row.SubrowId, _columns.Select(column => ReadCell(row, column)).ToArray());

    private LuminaStringRow ConvertStringRow(RawRow row) =>
        new(row.RowId, 0, ReadStringValues(column => ReadString(row, column)));

    private LuminaStringRow ConvertStringRow(RawSubrow row) =>
        new(row.RowId, row.SubrowId, ReadStringValues(column => ReadString(row, column)));

    private IReadOnlyList<LuminaStringValue> ReadStringValues(Func<HarmoniaColumnDefinition, string> read)
    {
        return _columns
            .Where(column => column.Type == HarmoniaColumnType.String)
            .OrderBy(column => column.Index)
            .Select(column => new LuminaStringValue(column.Index, read(column)))
            .ToArray();
    }

    private static string ReadString(RawRow row, HarmoniaColumnDefinition column) =>
        ((ReadOnlySeString)row.ReadStringColumn(column.Index)).ToMacroString();

    private static string ReadString(RawSubrow row, HarmoniaColumnDefinition column) =>
        ((ReadOnlySeString)row.ReadStringColumn(column.Index)).ToMacroString();

    private static HarmoniaCellData ReadCell(RawRow row, HarmoniaColumnDefinition column) =>
        ReadCellCore(column, type => ReadRawValue(row, column.Index, type));

    private static HarmoniaCellData ReadCell(RawSubrow row, HarmoniaColumnDefinition column) =>
        ReadCellCore(column, type => ReadRawValue(row, column.Index, type));

    private static HarmoniaCellData ReadCellCore(
        HarmoniaColumnDefinition column,
        Func<HarmoniaColumnType, object> read)
    {
        if (column.Type == HarmoniaColumnType.String)
        {
            ReadOnlySeString seString = (ReadOnlySeString)read(column.Type);
            return HarmoniaCellData.String(column.Index, seString.ToMacroString(), seString.Data.ToArray());
        }

        return HarmoniaCellData.Technical(column.Index, column.Type, read(column.Type));
    }

    private static object ReadRawValue(RawRow row, int columnIndex, HarmoniaColumnType type) => type switch
    {
        HarmoniaColumnType.String => row.ReadStringColumn(columnIndex),
        HarmoniaColumnType.Bool => row.ReadBoolColumn(columnIndex),
        HarmoniaColumnType.Int8 => row.ReadInt8Column(columnIndex),
        HarmoniaColumnType.UInt8 => row.ReadUInt8Column(columnIndex),
        HarmoniaColumnType.Int16 => row.ReadInt16Column(columnIndex),
        HarmoniaColumnType.UInt16 => row.ReadUInt16Column(columnIndex),
        HarmoniaColumnType.Int32 => row.ReadInt32Column(columnIndex),
        HarmoniaColumnType.UInt32 => row.ReadUInt32Column(columnIndex),
        HarmoniaColumnType.Int64 => row.ReadInt64Column(columnIndex),
        HarmoniaColumnType.UInt64 => row.ReadUInt64Column(columnIndex),
        HarmoniaColumnType.Float32 => row.ReadFloat32Column(columnIndex),
        _ when IsPacked(type) => row.ReadPackedBoolColumn(columnIndex, PackedBit(type)),
        _ => throw new NotSupportedException($"Unsupported Harmonia column type: {type}."),
    };

    private static object ReadRawValue(RawSubrow row, int columnIndex, HarmoniaColumnType type) => type switch
    {
        HarmoniaColumnType.String => row.ReadStringColumn(columnIndex),
        HarmoniaColumnType.Bool => row.ReadBoolColumn(columnIndex),
        HarmoniaColumnType.Int8 => row.ReadInt8Column(columnIndex),
        HarmoniaColumnType.UInt8 => row.ReadUInt8Column(columnIndex),
        HarmoniaColumnType.Int16 => row.ReadInt16Column(columnIndex),
        HarmoniaColumnType.UInt16 => row.ReadUInt16Column(columnIndex),
        HarmoniaColumnType.Int32 => row.ReadInt32Column(columnIndex),
        HarmoniaColumnType.UInt32 => row.ReadUInt32Column(columnIndex),
        HarmoniaColumnType.Int64 => row.ReadInt64Column(columnIndex),
        HarmoniaColumnType.UInt64 => row.ReadUInt64Column(columnIndex),
        HarmoniaColumnType.Float32 => row.ReadFloat32Column(columnIndex),
        _ when IsPacked(type) => row.ReadPackedBoolColumn(columnIndex, PackedBit(type)),
        _ => throw new NotSupportedException($"Unsupported Harmonia column type: {type}."),
    };

    private static bool IsPacked(HarmoniaColumnType type) => type is >= HarmoniaColumnType.PackedBool0 and <= HarmoniaColumnType.PackedBool7;

    private static byte PackedBit(HarmoniaColumnType type) => checked((byte)((int)type - (int)HarmoniaColumnType.PackedBool0));
}
