using System.Globalization;
using System.Buffers.Binary;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Hxs;

public static class HxsHashing
{
    public static byte[] HashMacro(string macroText) =>
        CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-MACRO");
            hasher.WriteUtf8(macroText);
        });

    public static byte[] HashRaw(ReadOnlySpan<byte> rawValue)
    {
        byte[] rawCopy = rawValue.ToArray();
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-RAW-STRING");
            hasher.WriteLengthPrefixedBytes(rawCopy);
        });
    }

    public static byte[] EncodeTechnicalPayload(IReadOnlyList<HxsTechnicalCell> cells)
    {
        CanonicalHasher hasher = new();
        foreach (HxsTechnicalCell cell in cells.OrderBy(cell => cell.ColumnIndex))
        {
            WriteTechnicalCell(hasher, cell);
        }

        return hasher.CanonicalBytes.ToArray();
    }

    public static byte[] HashRowTechnical(
        string sheetName,
        uint rowId,
        ushort subrowId,
        IReadOnlyList<HxsTechnicalCell> cells)
    {
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-ROW-TECHNICAL");
            WriteRowIdentity(hasher, sheetName, rowId, subrowId);
            foreach (HxsTechnicalCell cell in cells.OrderBy(cell => cell.ColumnIndex))
            {
                WriteTechnicalCell(hasher, cell);
            }
        });
    }

    public static byte[] HashRowStrings(
        string sheetName,
        uint rowId,
        ushort subrowId,
        IReadOnlyList<HxsStringCellRecord> cells)
    {
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-ROW-STRINGS");
            WriteRowIdentity(hasher, sheetName, rowId, subrowId);
            foreach (HxsStringCellRecord cell in cells.OrderBy(cell => cell.ColumnIndex))
            {
                hasher.WriteUInt32(checked((uint)cell.ColumnIndex));
                hasher.WriteBytes(cell.MacroHash);
                hasher.WriteByte(cell.RawHash is null ? (byte)0 : (byte)1);
                if (cell.RawHash is not null)
                {
                    hasher.WriteBytes(cell.RawHash);
                }
            }
        });
    }

    public static byte[] HashRow(
        string sheetName,
        uint rowId,
        ushort subrowId,
        ReadOnlySpan<byte> technicalHash,
        ReadOnlySpan<byte> stringHash)
    {
        byte[] technicalCopy = technicalHash.ToArray();
        byte[] stringCopy = stringHash.ToArray();
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-ROW");
            WriteRowIdentity(hasher, sheetName, rowId, subrowId);
            hasher.WriteBytes(technicalCopy);
            hasher.WriteBytes(stringCopy);
        });
    }

    public static byte[] HashSchema(
        string sheetName,
        HarmoniaSheetVariant variant,
        IReadOnlyList<HarmoniaColumnDefinition> columns)
    {
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-SCHEMA");
            hasher.WriteUtf8(sheetName);
            hasher.WriteUInt32((uint)variant);
            foreach (HarmoniaColumnDefinition column in columns.OrderBy(column => column.Index))
            {
                hasher.WriteUInt32(checked((uint)column.Index));
                hasher.WriteUInt32(checked((uint)column.Offset));
                hasher.WriteUInt32(checked((uint)column.Type));
            }
        });
    }

    public static byte[] HashSheetTechnical(string sheetName, IReadOnlyList<HxsRowRecord> rows)
    {
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-SHEET-TECHNICAL");
            hasher.WriteUtf8(sheetName);
            foreach (HxsRowRecord row in rows.OrderBy(row => row.RowId).ThenBy(row => row.SubrowId))
            {
                hasher.WriteUInt32(row.RowId);
                hasher.WriteUInt32(row.SubrowId);
                hasher.WriteBytes(row.TechnicalHash);
            }
        });
    }

    public static byte[] HashSheetStrings(string sheetName, IReadOnlyList<HxsRowRecord> rows)
    {
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-SHEET-STRINGS");
            hasher.WriteUtf8(sheetName);
            foreach (HxsRowRecord row in rows.OrderBy(row => row.RowId).ThenBy(row => row.SubrowId))
            {
                hasher.WriteUInt32(row.RowId);
                hasher.WriteUInt32(row.SubrowId);
                hasher.WriteBytes(row.StringHash);
            }
        });
    }

    public static byte[] HashSheetContent(
        string sheetName,
        HarmoniaSheetVariant variant,
        ReadOnlySpan<byte> schemaHash,
        ReadOnlySpan<byte> technicalHash,
        ReadOnlySpan<byte> stringHash)
    {
        byte[] schemaCopy = schemaHash.ToArray();
        byte[] technicalCopy = technicalHash.ToArray();
        byte[] stringCopy = stringHash.ToArray();
        return CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-V1-SHEET");
            hasher.WriteUtf8(sheetName);
            hasher.WriteUInt32((uint)variant);
            hasher.WriteBytes(schemaCopy);
            hasher.WriteBytes(technicalCopy);
            hasher.WriteBytes(stringCopy);
        });
    }

    public static string ComputeContentId(string language, IReadOnlyList<HxsSheetRecord> sheets)
    {
        byte[] hash = CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-CONTENT-v1");
            hasher.WriteUtf8(language);
            foreach (HxsSheetRecord sheet in sheets.OrderBy(sheet => sheet.Name, StringComparer.Ordinal))
            {
                hasher.WriteUtf8(sheet.Name);
                hasher.WriteUtf8(sheet.EffectiveLanguage);
                hasher.WriteBytes(sheet.SchemaHash);
                hasher.WriteBytes(sheet.ContentHash);
            }
        });

        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSnapshotId(string gameVersion, string language, string contentId)
    {
        byte[] hash = CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain("HARMONIA-HXS-SNAPSHOT-v1");
            hasher.WriteUtf8(gameVersion);
            hasher.WriteUtf8(language);
            hasher.WriteUtf8(contentId);
        });

        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] EncodeTechnicalValue(HarmoniaColumnType type, object value)
    {
        CanonicalHasher hasher = new();
        switch (type)
        {
            case HarmoniaColumnType.Bool:
            case HarmoniaColumnType.PackedBool0:
            case HarmoniaColumnType.PackedBool1:
            case HarmoniaColumnType.PackedBool2:
            case HarmoniaColumnType.PackedBool3:
            case HarmoniaColumnType.PackedBool4:
            case HarmoniaColumnType.PackedBool5:
            case HarmoniaColumnType.PackedBool6:
            case HarmoniaColumnType.PackedBool7:
                hasher.WriteByte((bool)value ? (byte)1 : (byte)0);
                break;
            case HarmoniaColumnType.Int8:
                hasher.WriteByte(unchecked((byte)(sbyte)value));
                break;
            case HarmoniaColumnType.UInt8:
                hasher.WriteByte((byte)value);
                break;
            case HarmoniaColumnType.Int16:
                hasher.WriteUInt16(unchecked((ushort)(short)value));
                break;
            case HarmoniaColumnType.UInt16:
                hasher.WriteUInt16((ushort)value);
                break;
            case HarmoniaColumnType.Int32:
                hasher.WriteUInt32(unchecked((uint)(int)value));
                break;
            case HarmoniaColumnType.UInt32:
                hasher.WriteUInt32((uint)value);
                break;
            case HarmoniaColumnType.Int64:
                hasher.WriteUInt64(unchecked((ulong)(long)value));
                break;
            case HarmoniaColumnType.UInt64:
                hasher.WriteUInt64((ulong)value);
                break;
            case HarmoniaColumnType.Float32:
                hasher.WriteSingle((float)value);
                break;
            case HarmoniaColumnType.String:
                throw new ArgumentException("String cells do not have technical values.", nameof(type));
            default:
                throw new NotSupportedException($"Unsupported Harmonia column type: {type}.");
        }

        return hasher.CanonicalBytes.ToArray();
    }

    public static int ExpectedTechnicalValueLength(HarmoniaColumnType type) => type switch
    {
        HarmoniaColumnType.String => 0,
        HarmoniaColumnType.Bool => 1,
        HarmoniaColumnType.Int8 or HarmoniaColumnType.UInt8 => 1,
        HarmoniaColumnType.Int16 or HarmoniaColumnType.UInt16 => 2,
        HarmoniaColumnType.Int32 or HarmoniaColumnType.UInt32 or HarmoniaColumnType.Float32 => 4,
        HarmoniaColumnType.Int64 or HarmoniaColumnType.UInt64 => 8,
        HarmoniaColumnType.PackedBool0 or HarmoniaColumnType.PackedBool1 or
            HarmoniaColumnType.PackedBool2 or HarmoniaColumnType.PackedBool3 or
            HarmoniaColumnType.PackedBool4 or HarmoniaColumnType.PackedBool5 or
            HarmoniaColumnType.PackedBool6 or HarmoniaColumnType.PackedBool7 => 1,
        _ => throw new NotSupportedException($"Unsupported Harmonia column type: {type}."),
    };

    public static IReadOnlyList<HxsTechnicalCell> DecodeTechnicalPayload(
        ReadOnlySpan<byte> payload,
        IReadOnlyList<HarmoniaColumnDefinition> columns)
    {
        Dictionary<int, HarmoniaColumnDefinition> columnsByIndex = columns.ToDictionary(column => column.Index);
        List<HxsTechnicalCell> cells = new();
        int offset = 0;
        int previousColumn = -1;
        while (offset < payload.Length)
        {
            uint columnIndexValue = ReadUInt32(payload, ref offset);
            uint typeValue = ReadUInt32(payload, ref offset);
            uint valueLength = ReadUInt32(payload, ref offset);
            if (columnIndexValue > int.MaxValue || typeValue > int.MaxValue || valueLength > int.MaxValue)
            {
                throw new HxsFormatException("Technical payload contains an out-of-range value.");
            }

            int columnIndex = (int)columnIndexValue;
            int typeCode = (int)typeValue;
            if (columnIndex <= previousColumn || !columnsByIndex.TryGetValue(columnIndex, out HarmoniaColumnDefinition? column))
            {
                throw new HxsFormatException("Technical payload columns are not strictly ordered or are not in the sheet schema.");
            }

            if (!Enum.IsDefined((HarmoniaColumnType)typeCode) || (int)column.Type != typeCode || column.Type == HarmoniaColumnType.String)
            {
                throw new HxsFormatException($"Technical payload type does not match column {columnIndex}.");
            }

            int length = (int)valueLength;
            if (length != ExpectedTechnicalValueLength(column.Type) || length > payload.Length - offset)
            {
                throw new HxsFormatException($"Technical payload value length is invalid for column {columnIndex}.");
            }

            byte[] value = payload.Slice(offset, length).ToArray();
            offset += length;
            if (column.Type is HarmoniaColumnType.Bool or
                HarmoniaColumnType.PackedBool0 or HarmoniaColumnType.PackedBool1 or
                HarmoniaColumnType.PackedBool2 or HarmoniaColumnType.PackedBool3 or
                HarmoniaColumnType.PackedBool4 or HarmoniaColumnType.PackedBool5 or
                HarmoniaColumnType.PackedBool6 or HarmoniaColumnType.PackedBool7)
            {
                if (value[0] > 1)
                {
                    throw new HxsFormatException("Boolean technical payload values must be 0x00 or 0x01.");
                }
            }

            cells.Add(new HxsTechnicalCell(columnIndex, column.Type, value));
            previousColumn = columnIndex;
        }

        int expectedTechnicalColumns = columns.Count(column => column.Type != HarmoniaColumnType.String);
        if (cells.Count != expectedTechnicalColumns)
        {
            throw new HxsFormatException("Technical payload does not contain every non-String column exactly once.");
        }

        return cells;
    }

    private static void WriteTechnicalCell(CanonicalHasher hasher, HxsTechnicalCell cell)
    {
        hasher.WriteUInt32(checked((uint)cell.ColumnIndex));
        hasher.WriteUInt32(checked((uint)cell.Type));
        hasher.WriteLengthPrefixedBytes(cell.CanonicalValue);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (bytes.Length - offset < sizeof(uint))
        {
            throw new HxsFormatException("Technical payload is truncated.");
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        offset += sizeof(uint);
        return value;
    }

    private static void WriteRowIdentity(CanonicalHasher hasher, string sheetName, uint rowId, ushort subrowId)
    {
        hasher.WriteUtf8(sheetName);
        hasher.WriteUInt32(rowId);
        hasher.WriteUInt32(subrowId);
    }
}

public sealed class HxsSheetHashAccumulator
{
    private readonly CanonicalHasher _technicalHasher = new();
    private readonly CanonicalHasher _stringHasher = new();

    public HxsSheetHashAccumulator(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        _technicalHasher.WriteDomain("HARMONIA-HXS-V1-SHEET-TECHNICAL");
        _technicalHasher.WriteUtf8(sheetName);
        _stringHasher.WriteDomain("HARMONIA-HXS-V1-SHEET-STRINGS");
        _stringHasher.WriteUtf8(sheetName);
    }

    public void AddRow(HxsRowRecord row)
    {
        _technicalHasher.WriteUInt32(row.RowId);
        _technicalHasher.WriteUInt32(row.SubrowId);
        _technicalHasher.WriteBytes(row.TechnicalHash);

        _stringHasher.WriteUInt32(row.RowId);
        _stringHasher.WriteUInt32(row.SubrowId);
        _stringHasher.WriteBytes(row.StringHash);
    }

    public byte[] ComputeTechnicalHash() => _technicalHasher.ComputeHash();

    public byte[] ComputeStringHash() => _stringHasher.ComputeHash();
}
