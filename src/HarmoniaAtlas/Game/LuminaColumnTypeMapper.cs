using Lumina.Data.Structs.Excel;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Game;

public static class LuminaColumnTypeMapper
{
    public static HarmoniaColumnType Map(ExcelColumnDataType columnType) => columnType switch
    {
        ExcelColumnDataType.String => HarmoniaColumnType.String,
        ExcelColumnDataType.Bool => HarmoniaColumnType.Bool,
        ExcelColumnDataType.Int8 => HarmoniaColumnType.Int8,
        ExcelColumnDataType.UInt8 => HarmoniaColumnType.UInt8,
        ExcelColumnDataType.Int16 => HarmoniaColumnType.Int16,
        ExcelColumnDataType.UInt16 => HarmoniaColumnType.UInt16,
        ExcelColumnDataType.Int32 => HarmoniaColumnType.Int32,
        ExcelColumnDataType.UInt32 => HarmoniaColumnType.UInt32,
        ExcelColumnDataType.Int64 => HarmoniaColumnType.Int64,
        ExcelColumnDataType.UInt64 => HarmoniaColumnType.UInt64,
        ExcelColumnDataType.Float32 => HarmoniaColumnType.Float32,
        ExcelColumnDataType.PackedBool0 => HarmoniaColumnType.PackedBool0,
        ExcelColumnDataType.PackedBool1 => HarmoniaColumnType.PackedBool1,
        ExcelColumnDataType.PackedBool2 => HarmoniaColumnType.PackedBool2,
        ExcelColumnDataType.PackedBool3 => HarmoniaColumnType.PackedBool3,
        ExcelColumnDataType.PackedBool4 => HarmoniaColumnType.PackedBool4,
        ExcelColumnDataType.PackedBool5 => HarmoniaColumnType.PackedBool5,
        ExcelColumnDataType.PackedBool6 => HarmoniaColumnType.PackedBool6,
        ExcelColumnDataType.PackedBool7 => HarmoniaColumnType.PackedBool7,
        _ => throw new NotSupportedException($"Unsupported Lumina Excel column type: {columnType}."),
    };
}
