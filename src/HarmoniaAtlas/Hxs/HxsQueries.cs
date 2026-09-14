using Microsoft.Data.Sqlite;

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
}
