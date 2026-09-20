using Microsoft.Data.Sqlite;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Hxs;

internal static class HxsQueries
{
    public static HxsMetadata ReadMetadata(SqliteConnection connection)
    {
        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM hxs_meta;";
        long count = Convert.ToInt64(countCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (count != 1)
        {
            throw new HxsFormatException($"HXS metadata must contain exactly one row; found {count}.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT format_version, game_version, language, scope, content_id, snapshot_id, extractor_version, lumina_version, sheet_count, row_count, string_cell_count FROM hxs_meta WHERE id = 1;";
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new HxsFormatException("HXS metadata row id 1 is missing.");
        }

        return new HxsMetadata(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10));
    }

    public static IReadOnlyList<HxsSheetRecord> ReadSheets(SqliteConnection connection)
    {
        List<StoredSheetMetadata> storedSheets = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, variant, effective_language, column_count, row_count, schema_hash, technical_hash, string_hash, content_hash FROM sheets ORDER BY name COLLATE BINARY;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int sheetId = reader.GetInt32(0);
            int variantValue = reader.GetInt32(2);
            if (!Enum.IsDefined((HarmoniaSheetVariant)variantValue))
            {
                throw new HxsFormatException($"Unsupported HXS sheet variant: {variantValue}.");
            }

            storedSheets.Add(new StoredSheetMetadata(
                sheetId,
                reader.GetString(1),
                (HarmoniaSheetVariant)variantValue,
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                ReadHash(reader, 6, "schema_hash"),
                ReadHash(reader, 7, "technical_hash"),
                ReadHash(reader, 8, "string_hash"),
                ReadHash(reader, 9, "content_hash")));
        }

        List<HxsSheetRecord> sheets = new();
        foreach (StoredSheetMetadata storedSheet in storedSheets)
        {
            IReadOnlyList<HarmoniaColumnDefinition> columns = ReadColumns(connection, storedSheet.Id);
            if (storedSheet.ColumnCount != columns.Count)
            {
                throw new HxsFormatException($"Sheet '{storedSheet.Name}' column count does not match its columns.");
            }

            sheets.Add(new HxsSheetRecord(
                storedSheet.Name,
                storedSheet.Variant,
                storedSheet.EffectiveLanguage,
                columns,
                storedSheet.RowCount,
                storedSheet.SchemaHash,
                storedSheet.TechnicalHash,
                storedSheet.StringHash,
                storedSheet.ContentHash));
        }

        return sheets;
    }

    public static IEnumerable<HxsStringRowRecord> ReadStringRows(SqliteConnection connection, string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.row_id, r.subrow_id, sc.column_index, sc.macro_text
            FROM "rows" AS r
            LEFT JOIN string_cells AS sc
              ON sc.sheet_id = r.sheet_id AND sc.row_id = r.row_id AND sc.subrow_id = r.subrow_id
            INNER JOIN sheets AS s ON s.id = r.sheet_id
            WHERE s.name = $sheet_name
            ORDER BY r.row_id, r.subrow_id, sc.column_index;
            """;
        command.Parameters.AddWithValue("$sheet_name", sheetName);
        using SqliteDataReader reader = command.ExecuteReader();

        HxsStringRowRecord? current = null;
        while (reader.Read())
        {
            uint rowId = ReadUInt32(reader, 0, "row_id");
            ushort subrowId = ReadUInt16(reader, 1, "subrow_id");
            if (current is null || current.RowId != rowId || current.SubrowId != subrowId)
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = new HxsStringRowRecord(rowId, subrowId, new List<HxsStringOccurrenceValue>());
            }

            if (!reader.IsDBNull(2))
            {
                int columnIndex = reader.GetInt32(2);
                string macroText = reader.GetString(3);
                ((List<HxsStringOccurrenceValue>)current.Values).Add(
                    new HxsStringOccurrenceValue(rowId, subrowId, columnIndex, macroText));
            }
        }

        if (current is not null)
        {
            yield return current;
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
            int offset = reader.GetInt32(1);
            int typeValue = reader.GetInt32(2);
            if (index <= previousIndex || offset < 0 || !Enum.IsDefined((HarmoniaColumnType)typeValue))
            {
                throw new HxsFormatException($"Invalid HXS column definition in sheet {sheetId}.");
            }

            columns.Add(new HarmoniaColumnDefinition(index, offset, (HarmoniaColumnType)typeValue));
            previousIndex = index;
        }

        return columns;
    }

    private static byte[] ReadHash(SqliteDataReader reader, int ordinal, string fieldName)
    {
        if (reader.IsDBNull(ordinal) || reader.GetValue(ordinal) is not byte[] value || value.Length != 32)
        {
            throw new HxsFormatException($"HXS {fieldName} must contain a SHA-256 hash.");
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

    private sealed record StoredSheetMetadata(
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
}
