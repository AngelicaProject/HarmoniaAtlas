using Microsoft.Data.Sqlite;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Hxs;

public sealed record HxsVerificationResult(
    int ApplicationId,
    int FormatVersion,
    bool IntegrityCheckPassed,
    HxsMetadata Metadata);

public static class HxsVerifier
{
    public static HxsVerificationResult Verify(string path)
    {
        using HxsReader reader = HxsReader.OpenReadOnly(path);
        HxsDatabase database = reader.Database;
        HxsSchema.Validate(database.Connection);
        if (!reader.RunIntegrityCheck())
        {
            throw new HxsFormatException("SQLite integrity check failed.");
        }

        if (!database.RunForeignKeyCheck())
        {
            throw new HxsFormatException("SQLite foreign key integrity check failed.");
        }

        HxsMetadata metadata = reader.Metadata;
        ValidateMetadata(metadata);
        List<HxsSheetRecord> sheets = new();
        long actualRowCount = 0;
        long actualStringCount = 0;

        foreach (StoredSheet storedSheet in ReadSheets(database.Connection).ToArray())
        {
            IReadOnlyList<HarmoniaColumnDefinition> columns = ReadColumns(database.Connection, storedSheet.Id);
            if (storedSheet.ColumnCount != columns.Count)
            {
                throw new HxsFormatException($"Sheet '{storedSheet.Name}' column count does not match its columns.");
            }

            byte[] schemaHash = HxsHashing.HashSchema(storedSheet.Name, storedSheet.Variant, columns);
            RequireHashEqual(storedSheet.SchemaHash, schemaHash, $"sheet schema hash for '{storedSheet.Name}'");
            HxsSheetHashAccumulator hashAccumulator = new(storedSheet.Name);
            int rowCount = 0;
            long stringCount = 0;
            VerifyRows(
                database.Connection,
                storedSheet,
                columns,
                hashAccumulator,
                ref rowCount,
                ref stringCount);

            if (storedSheet.RowCount != rowCount)
            {
                throw new HxsFormatException($"Sheet '{storedSheet.Name}' row count does not match its rows.");
            }

            byte[] technicalHash = hashAccumulator.ComputeTechnicalHash();
            byte[] stringHash = hashAccumulator.ComputeStringHash();
            RequireHashEqual(storedSheet.TechnicalHash, technicalHash, $"sheet technical hash for '{storedSheet.Name}'");
            RequireHashEqual(storedSheet.StringHash, stringHash, $"sheet string hash for '{storedSheet.Name}'");
            byte[] contentHash = HxsHashing.HashSheetContent(
                storedSheet.Name,
                storedSheet.Variant,
                schemaHash,
                technicalHash,
                stringHash);
            RequireHashEqual(storedSheet.ContentHash, contentHash, $"sheet content hash for '{storedSheet.Name}'");

            HxsSheetRecord sheet = new(
                storedSheet.Name,
                storedSheet.Variant,
                storedSheet.EffectiveLanguage,
                columns,
                rowCount,
                schemaHash,
                technicalHash,
                stringHash,
                contentHash);
            sheets.Add(sheet);
            actualRowCount = checked(actualRowCount + rowCount);
            actualStringCount = checked(actualStringCount + stringCount);
        }

        if (metadata.SheetCount != sheets.Count || metadata.RowCount != actualRowCount || metadata.StringCellCount != actualStringCount)
        {
            throw new HxsFormatException("HXS metadata counts do not match the stored artifact.");
        }

        string contentId = HxsHashing.ComputeContentId(metadata.Language, sheets);
        if (!string.Equals(metadata.ContentId, contentId, StringComparison.Ordinal))
        {
            throw new HxsFormatException("HXS content_id does not match the stored source content.");
        }

        string snapshotId = HxsHashing.ComputeSnapshotId(metadata.GameVersion, metadata.Language, contentId);
        if (!string.Equals(metadata.SnapshotId, snapshotId, StringComparison.Ordinal))
        {
            throw new HxsFormatException("HXS snapshot_id does not match the stored source snapshot.");
        }

        return new HxsVerificationResult(reader.ApplicationId, reader.FormatVersion, true, metadata);
    }

    private static IEnumerable<StoredSheet> ReadSheets(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, variant, effective_language, column_count, row_count, schema_hash, technical_hash, string_hash, content_hash FROM sheets ORDER BY id;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int variantValue = reader.GetInt32(2);
            if (!Enum.IsDefined((HarmoniaSheetVariant)variantValue))
            {
                throw new HxsFormatException($"Unsupported HXS sheet variant: {variantValue}.");
            }

            yield return new StoredSheet(
                reader.GetInt32(0),
                reader.GetString(1),
                (HarmoniaSheetVariant)variantValue,
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                ReadHash(reader, 6, "schema_hash"),
                ReadHash(reader, 7, "technical_hash"),
                ReadHash(reader, 8, "string_hash"),
                ReadHash(reader, 9, "content_hash"));
        }
    }

    private static IReadOnlyList<HarmoniaColumnDefinition> ReadColumns(SqliteConnection connection, int sheetId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT column_index, offset, type FROM columns WHERE sheet_id = $sheet_id ORDER BY column_index;";
        command.Parameters.AddWithValue("$sheet_id", sheetId);
        using SqliteDataReader reader = command.ExecuteReader();
        List<HarmoniaColumnDefinition> columns = new();
        int previousIndex = -1;
        while (reader.Read())
        {
            int index = reader.GetInt32(0);
            int typeValue = reader.GetInt32(2);
            if (index <= previousIndex || reader.GetInt32(1) < 0 || !Enum.IsDefined((HarmoniaColumnType)typeValue))
            {
                throw new HxsFormatException($"Invalid HXS column definition in sheet {sheetId}.");
            }

            columns.Add(new HarmoniaColumnDefinition(index, reader.GetInt32(1), (HarmoniaColumnType)typeValue));
            previousIndex = index;
        }

        return columns;
    }

    private static void VerifyRows(
        SqliteConnection connection,
        StoredSheet sheet,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        HxsSheetHashAccumulator hashAccumulator,
        ref int rowCount,
        ref long stringCount)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.row_id, r.subrow_id, r.technical_payload, r.row_hash, r.technical_hash, r.string_hash,
                   sc.column_index, sc.macro_text, sc.raw_value, sc.macro_hash, sc.raw_hash
            FROM "rows" AS r
            LEFT JOIN string_cells AS sc
              ON sc.sheet_id = r.sheet_id AND sc.row_id = r.row_id AND sc.subrow_id = r.subrow_id
            WHERE r.sheet_id = $sheet_id
            ORDER BY r.row_id, r.subrow_id, sc.column_index;
            """;
        command.Parameters.AddWithValue("$sheet_id", sheet.Id);
        using SqliteDataReader reader = command.ExecuteReader();

        StoredRow? current = null;
        while (reader.Read())
        {
            uint rowId = ReadUInt32(reader, 0, "row_id");
            ushort subrowId = ReadUInt16(reader, 1, "subrow_id");
            if (current is null || current.RowId != rowId || current.SubrowId != subrowId)
            {
                if (current is not null)
                {
                    FinalizeRow(sheet, columns, current, hashAccumulator, ref rowCount, ref stringCount);
                }

                current = new StoredRow(
                    rowId,
                    subrowId,
                    ReadBlob(reader, 2, "technical_payload"),
                    ReadHash(reader, 3, "row_hash"),
                    ReadHash(reader, 4, "technical_hash"),
                    ReadHash(reader, 5, "string_hash"),
                    new List<HxsStringCellRecord>());
            }

            if (!reader.IsDBNull(6))
            {
                int columnIndex = reader.GetInt32(6);
                string macroText = reader.GetString(7);
                byte[]? rawValue = reader.IsDBNull(8) ? null : (byte[])reader.GetValue(8);
                byte[] macroHash = ReadHash(reader, 9, "macro_hash");
                byte[]? rawHash = reader.IsDBNull(10) ? null : ReadHash(reader, 10, "raw_hash");
                byte[] expectedMacroHash = HxsHashing.HashMacro(macroText);
                RequireHashEqual(macroHash, expectedMacroHash, $"macro hash for {sheet.Name}/{rowId}/{subrowId}/{columnIndex}");
                if (rawValue is null)
                {
                    if (rawHash is not null)
                    {
                        throw new HxsFormatException($"Raw hash is present without raw bytes for {sheet.Name}/{rowId}/{subrowId}/{columnIndex}.");
                    }
                }
                else
                {
                    if (rawHash is null)
                    {
                        throw new HxsFormatException($"Raw bytes are present without a raw hash for {sheet.Name}/{rowId}/{subrowId}/{columnIndex}.");
                    }

                    RequireHashEqual(rawHash, HxsHashing.HashRaw(rawValue), $"raw hash for {sheet.Name}/{rowId}/{subrowId}/{columnIndex}");
                }

                current.StringCells.Add(new HxsStringCellRecord(rowId, subrowId, columnIndex, macroText, rawValue, macroHash, rawHash));
                stringCount = checked(stringCount + 1);
            }
        }

        if (current is not null)
        {
            FinalizeRow(sheet, columns, current, hashAccumulator, ref rowCount, ref stringCount);
        }
    }

    private static void FinalizeRow(
        StoredSheet sheet,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        StoredRow row,
        HxsSheetHashAccumulator hashAccumulator,
        ref int rowCount,
        ref long stringCount)
    {
        IReadOnlyList<HxsTechnicalCell> technicalCells = HxsHashing.DecodeTechnicalPayload(row.TechnicalPayload, columns);
        byte[] canonicalPayload = HxsHashing.EncodeTechnicalPayload(technicalCells);
        if (!canonicalPayload.AsSpan().SequenceEqual(row.TechnicalPayload))
        {
            throw new HxsFormatException($"Technical payload is not canonical for {sheet.Name}/{row.RowId}/{row.SubrowId}.");
        }

        HashSet<int> expectedStringColumns = columns.Where(column => column.Type == HarmoniaColumnType.String).Select(column => column.Index).ToHashSet();
        if (row.StringCells.Count != expectedStringColumns.Count || row.StringCells.Select(cell => cell.ColumnIndex).ToHashSet().Count != row.StringCells.Count)
        {
            throw new HxsFormatException($"String cell coverage is invalid for {sheet.Name}/{row.RowId}/{row.SubrowId}.");
        }

        foreach (HxsStringCellRecord cell in row.StringCells)
        {
            if (!expectedStringColumns.Contains(cell.ColumnIndex))
            {
                throw new HxsFormatException($"String cell column {cell.ColumnIndex} is not a String column in sheet '{sheet.Name}'.");
            }
        }

        byte[] technicalHash = HxsHashing.HashRowTechnical(sheet.Name, row.RowId, row.SubrowId, technicalCells);
        byte[] stringHash = HxsHashing.HashRowStrings(sheet.Name, row.RowId, row.SubrowId, row.StringCells);
        byte[] rowHash = HxsHashing.HashRow(sheet.Name, row.RowId, row.SubrowId, technicalHash, stringHash);
        RequireHashEqual(row.TechnicalHash, technicalHash, $"row technical hash for {sheet.Name}/{row.RowId}/{row.SubrowId}");
        RequireHashEqual(row.StringHash, stringHash, $"row string hash for {sheet.Name}/{row.RowId}/{row.SubrowId}");
        RequireHashEqual(row.RowHash, rowHash, $"row hash for {sheet.Name}/{row.RowId}/{row.SubrowId}");

        hashAccumulator.AddRow(new HxsRowRecord(
            row.RowId,
            row.SubrowId,
            row.TechnicalPayload,
            row.RowHash,
            technicalHash,
            stringHash,
            row.StringCells));
        rowCount = checked(rowCount + 1);
    }

    private static void ValidateMetadata(HxsMetadata metadata)
    {
        if (metadata.FormatVersion != HxsFormatVersion.Current || metadata.Scope != "full" ||
            string.IsNullOrWhiteSpace(metadata.GameVersion) || string.IsNullOrWhiteSpace(metadata.Language) ||
            string.IsNullOrWhiteSpace(metadata.ExtractorVersion) || string.IsNullOrWhiteSpace(metadata.LuminaVersion) ||
            metadata.SheetCount < 0 || metadata.RowCount < 0 || metadata.StringCellCount < 0)
        {
            throw new HxsFormatException("HXS metadata is invalid.");
        }
    }

    private static byte[] ReadHash(SqliteDataReader reader, int ordinal, string fieldName)
    {
        byte[] value = ReadBlob(reader, ordinal, fieldName);
        if (value.Length != 32)
        {
            throw new HxsFormatException($"HXS {fieldName} must contain a SHA-256 hash.");
        }

        return value;
    }

    private static byte[] ReadBlob(SqliteDataReader reader, int ordinal, string fieldName)
    {
        if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) is not byte[] value)
        {
            throw new HxsFormatException($"HXS {fieldName} must contain a BLOB.");
        }

        return value;
    }

    private static uint ReadUInt32(SqliteDataReader reader, int ordinal, string fieldName)
    {
        long value = reader.GetInt64(ordinal);
        if (value < 0 || value > uint.MaxValue)
        {
            throw new HxsFormatException($"HXS {fieldName} is out of range.");
        }

        return (uint)value;
    }

    private static ushort ReadUInt16(SqliteDataReader reader, int ordinal, string fieldName)
    {
        long value = reader.GetInt64(ordinal);
        if (value < 0 || value > ushort.MaxValue)
        {
            throw new HxsFormatException($"HXS {fieldName} is out of range.");
        }

        return (ushort)value;
    }

    private static void RequireHashEqual(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string description)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw new HxsFormatException($"HXS {description} does not match its canonical value.");
        }
    }

    private sealed record StoredSheet(
        int Id,
        string Name,
        HarmoniaSheetVariant Variant,
        string EffectiveLanguage,
        int ColumnCount,
        int RowCount,
        byte[] SchemaHash,
        byte[] TechnicalHash,
        byte[] StringHash,
        byte[] ContentHash);

    private sealed record StoredRow(
        uint RowId,
        ushort SubrowId,
        byte[] TechnicalPayload,
        byte[] RowHash,
        byte[] TechnicalHash,
        byte[] StringHash,
        List<HxsStringCellRecord> StringCells);
}
