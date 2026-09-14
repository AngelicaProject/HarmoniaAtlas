using Lumina.Data.Structs.Excel;
using HarmoniaAtlas.Game;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Tests;

public sealed class LuminaMappingTests
{
    [Fact]
    public void SupportedLuminaColumnTypesAreMapped()
    {
        ExcelColumnDataType[] supportedTypes =
        [
            ExcelColumnDataType.String,
            ExcelColumnDataType.Bool,
            ExcelColumnDataType.Int8,
            ExcelColumnDataType.UInt8,
            ExcelColumnDataType.Int16,
            ExcelColumnDataType.UInt16,
            ExcelColumnDataType.Int32,
            ExcelColumnDataType.UInt32,
            ExcelColumnDataType.Int64,
            ExcelColumnDataType.UInt64,
            ExcelColumnDataType.Float32,
            ExcelColumnDataType.PackedBool0,
            ExcelColumnDataType.PackedBool1,
            ExcelColumnDataType.PackedBool2,
            ExcelColumnDataType.PackedBool3,
            ExcelColumnDataType.PackedBool4,
            ExcelColumnDataType.PackedBool5,
            ExcelColumnDataType.PackedBool6,
            ExcelColumnDataType.PackedBool7,
        ];

        foreach (ExcelColumnDataType type in supportedTypes)
        {
            HarmoniaColumnType expected = Enum.Parse<HarmoniaColumnType>(type.ToString());
            Assert.Equal(expected, LuminaColumnTypeMapper.Map(type));
        }
    }

    [Fact]
    public void UnsupportedLuminaColumnTypeFailsExplicitly()
    {
        Assert.Throws<NotSupportedException>(() => LuminaColumnTypeMapper.Map(ExcelColumnDataType.Unk));
        Assert.Throws<NotSupportedException>(() => LuminaColumnTypeMapper.Map(ExcelColumnDataType.Unk2));
    }
}
