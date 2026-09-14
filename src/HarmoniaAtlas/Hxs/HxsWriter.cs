using Microsoft.Data.Sqlite;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Hxs;

public sealed class HxsWriter
{
    public HxsWriteSession Begin(string targetPath) => new(targetPath);

    public void WriteEmpty(string targetPath)
    {
        using HxsWriteSession session = Begin(targetPath);
        string contentId = HxsHashing.ComputeContentId("en", Array.Empty<HxsSheetRecord>());
        HxsMetadata metadata = new(
            HxsFormatVersion.Current,
            "test",
            "en",
            "full",
            contentId,
            HxsHashing.ComputeSnapshotId("test", "en", contentId),
            AtlasApplicationVersion.Current,
            LuminaVersion.Current,
            0,
            0,
            0);
        session.WriteMetadata(metadata);
        session.Complete();
    }
}

public sealed class HxsWriteSession : IDisposable
{
    private readonly string _targetPath;
    private readonly string _partialPath;
    private readonly HxsDatabase _database;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _insertSheet;
    private readonly SqliteCommand _insertColumn;
    private readonly SqliteCommand _updateSheet;
    private readonly SqliteCommand _insertRow;
    private readonly SqliteCommand _insertStringCell;
    private readonly SqliteCommand _insertMeta;
    private bool _metadataWritten;
    private bool _completed;
    private bool _disposed;
    private bool _transactionDisposed;
    private bool _commandsDisposed;
    private bool _databaseDisposed;

    public HxsWriteSession(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _targetPath = Path.GetFullPath(targetPath);
        _partialPath = _targetPath + ".partial";
        if (File.Exists(_targetPath))
        {
            throw new IOException($"HXS target already exists: {_targetPath}");
        }

        if (File.Exists(_partialPath))
        {
            throw new IOException($"HXS partial target already exists: {_partialPath}");
        }

        string? directory = Path.GetDirectoryName(_targetPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            _database = HxsDatabase.CreateNew(_partialPath);
            _database.SetHxsIdentity();
            HxsSchema.Create(_database);
            _transaction = _database.BeginTransaction();
            _insertSheet = Prepare(
                """
                INSERT INTO sheets (name, variant, effective_language, column_count, row_count, schema_hash, technical_hash, string_hash, content_hash)
                VALUES ($name, $variant, $effective_language, $column_count, $row_count, $schema_hash, $technical_hash, $string_hash, $content_hash);
                """,
                ("$name", SqliteType.Text),
                ("$variant", SqliteType.Integer),
                ("$effective_language", SqliteType.Text),
                ("$column_count", SqliteType.Integer),
                ("$row_count", SqliteType.Integer),
                ("$schema_hash", SqliteType.Blob),
                ("$technical_hash", SqliteType.Blob),
                ("$string_hash", SqliteType.Blob),
                ("$content_hash", SqliteType.Blob));
            _insertColumn = Prepare(
                "INSERT INTO columns (sheet_id, column_index, offset, type) VALUES ($sheet_id, $column_index, $offset, $type);",
                ("$sheet_id", SqliteType.Integer),
                ("$column_index", SqliteType.Integer),
                ("$offset", SqliteType.Integer),
                ("$type", SqliteType.Integer));
            _updateSheet = Prepare(
                "UPDATE sheets SET row_count = $row_count, schema_hash = $schema_hash, technical_hash = $technical_hash, string_hash = $string_hash, content_hash = $content_hash WHERE id = $id;",
                ("$row_count", SqliteType.Integer),
                ("$schema_hash", SqliteType.Blob),
                ("$technical_hash", SqliteType.Blob),
                ("$string_hash", SqliteType.Blob),
                ("$content_hash", SqliteType.Blob),
                ("$id", SqliteType.Integer));
            _insertRow = Prepare(
                "INSERT INTO \"rows\" (sheet_id, row_id, subrow_id, technical_payload, row_hash, technical_hash, string_hash) VALUES ($sheet_id, $row_id, $subrow_id, $technical_payload, $row_hash, $technical_hash, $string_hash);",
                ("$sheet_id", SqliteType.Integer),
                ("$row_id", SqliteType.Integer),
                ("$subrow_id", SqliteType.Integer),
                ("$technical_payload", SqliteType.Blob),
                ("$row_hash", SqliteType.Blob),
                ("$technical_hash", SqliteType.Blob),
                ("$string_hash", SqliteType.Blob));
            _insertStringCell = Prepare(
                "INSERT INTO string_cells (sheet_id, row_id, subrow_id, column_index, macro_text, raw_value, macro_hash, raw_hash) VALUES ($sheet_id, $row_id, $subrow_id, $column_index, $macro_text, $raw_value, $macro_hash, $raw_hash);",
                ("$sheet_id", SqliteType.Integer),
                ("$row_id", SqliteType.Integer),
                ("$subrow_id", SqliteType.Integer),
                ("$column_index", SqliteType.Integer),
                ("$macro_text", SqliteType.Text),
                ("$raw_value", SqliteType.Blob),
                ("$macro_hash", SqliteType.Blob),
                ("$raw_hash", SqliteType.Blob));
            _insertMeta = Prepare(
                "INSERT INTO hxs_meta (id, format_version, game_version, language, scope, content_id, snapshot_id, extractor_version, lumina_version, sheet_count, row_count, string_cell_count) VALUES (1, $format_version, $game_version, $language, $scope, $content_id, $snapshot_id, $extractor_version, $lumina_version, $sheet_count, $row_count, $string_cell_count);",
                ("$format_version", SqliteType.Integer),
                ("$game_version", SqliteType.Text),
                ("$language", SqliteType.Text),
                ("$scope", SqliteType.Text),
                ("$content_id", SqliteType.Text),
                ("$snapshot_id", SqliteType.Text),
                ("$extractor_version", SqliteType.Text),
                ("$lumina_version", SqliteType.Text),
                ("$sheet_count", SqliteType.Integer),
                ("$row_count", SqliteType.Integer),
                ("$string_cell_count", SqliteType.Integer));
        }
        catch
        {
            try
            {
                _database?.Dispose();
            }
            catch
            {
                // Preserve the constructor failure.
            }

            TryDeletePartial(_partialPath);
            throw;
        }
    }

    public int BeginSheet(HxsSheetRecord sheet)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sheet);
        SetParameter(_insertSheet, "$name", sheet.Name);
        SetParameter(_insertSheet, "$variant", (int)sheet.Variant);
        SetParameter(_insertSheet, "$effective_language", sheet.EffectiveLanguage);
        SetParameter(_insertSheet, "$column_count", sheet.Columns.Count);
        SetParameter(_insertSheet, "$row_count", 0);
        SetParameter(_insertSheet, "$schema_hash", sheet.SchemaHash);
        SetParameter(_insertSheet, "$technical_hash", ZeroHash);
        SetParameter(_insertSheet, "$string_hash", ZeroHash);
        SetParameter(_insertSheet, "$content_hash", ZeroHash);
        _insertSheet.ExecuteNonQuery();

        using SqliteCommand idCommand = _database.CreateCommand("SELECT last_insert_rowid();");
        idCommand.Transaction = _transaction;
        int sheetId = checked(Convert.ToInt32(idCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        foreach (HarmoniaColumnDefinition column in sheet.Columns.OrderBy(column => column.Index))
        {
            SetParameter(_insertColumn, "$sheet_id", sheetId);
            SetParameter(_insertColumn, "$column_index", column.Index);
            SetParameter(_insertColumn, "$offset", column.Offset);
            SetParameter(_insertColumn, "$type", (int)column.Type);
            _insertColumn.ExecuteNonQuery();
        }

        return sheetId;
    }

    public void WriteRow(int sheetId, HxsRowRecord row)
    {
        ThrowIfDisposed();
        SetParameter(_insertRow, "$sheet_id", sheetId);
        SetParameter(_insertRow, "$row_id", row.RowId);
        SetParameter(_insertRow, "$subrow_id", row.SubrowId);
        SetParameter(_insertRow, "$technical_payload", row.TechnicalPayload);
        SetParameter(_insertRow, "$row_hash", row.RowHash);
        SetParameter(_insertRow, "$technical_hash", row.TechnicalHash);
        SetParameter(_insertRow, "$string_hash", row.StringHash);
        _insertRow.ExecuteNonQuery();

        foreach (HxsStringCellRecord cell in row.StringCells.OrderBy(cell => cell.ColumnIndex))
        {
            SetParameter(_insertStringCell, "$sheet_id", sheetId);
            SetParameter(_insertStringCell, "$row_id", cell.RowId);
            SetParameter(_insertStringCell, "$subrow_id", cell.SubrowId);
            SetParameter(_insertStringCell, "$column_index", cell.ColumnIndex);
            SetParameter(_insertStringCell, "$macro_text", cell.MacroText);
            SetParameter(_insertStringCell, "$raw_value", cell.RawValue is null ? DBNull.Value : cell.RawValue);
            SetParameter(_insertStringCell, "$macro_hash", cell.MacroHash);
            SetParameter(_insertStringCell, "$raw_hash", cell.RawHash is null ? DBNull.Value : cell.RawHash);
            _insertStringCell.ExecuteNonQuery();
        }
    }

    public void CompleteSheet(int sheetId, HxsSheetRecord sheet)
    {
        ThrowIfDisposed();
        SetParameter(_updateSheet, "$row_count", sheet.RowCount);
        SetParameter(_updateSheet, "$schema_hash", sheet.SchemaHash);
        SetParameter(_updateSheet, "$technical_hash", sheet.TechnicalHash);
        SetParameter(_updateSheet, "$string_hash", sheet.StringHash);
        SetParameter(_updateSheet, "$content_hash", sheet.ContentHash);
        SetParameter(_updateSheet, "$id", sheetId);
        if (_updateSheet.ExecuteNonQuery() != 1)
        {
            throw new HxsFormatException($"Unable to complete HXS sheet '{sheet.Name}'.");
        }
    }

    public void WriteMetadata(HxsMetadata metadata)
    {
        ThrowIfDisposed();
        if (_metadataWritten)
        {
            throw new InvalidOperationException("HXS metadata has already been written.");
        }

        ArgumentNullException.ThrowIfNull(metadata);
        SetParameter(_insertMeta, "$format_version", metadata.FormatVersion);
        SetParameter(_insertMeta, "$game_version", metadata.GameVersion);
        SetParameter(_insertMeta, "$language", metadata.Language);
        SetParameter(_insertMeta, "$scope", metadata.Scope);
        SetParameter(_insertMeta, "$content_id", metadata.ContentId);
        SetParameter(_insertMeta, "$snapshot_id", metadata.SnapshotId);
        SetParameter(_insertMeta, "$extractor_version", metadata.ExtractorVersion);
        SetParameter(_insertMeta, "$lumina_version", metadata.LuminaVersion);
        SetParameter(_insertMeta, "$sheet_count", metadata.SheetCount);
        SetParameter(_insertMeta, "$row_count", metadata.RowCount);
        SetParameter(_insertMeta, "$string_cell_count", metadata.StringCellCount);
        _insertMeta.ExecuteNonQuery();
        _metadataWritten = true;
    }

    public void Complete()
    {
        ThrowIfDisposed();
        if (!_metadataWritten)
        {
            throw new InvalidOperationException("HXS metadata must be written before completion.");
        }

        _transaction.Commit();
        DisposeTransaction();
        DisposeCommands();
        DisposeDatabase();
        FlushToDisk(_partialPath);

        HxsVerifier.Verify(_partialPath);
        File.Move(_partialPath, _targetPath);
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_completed)
        {
            RollbackAndDisposeTransactionSafely();
        }

        DisposeCommands();
        DisposeDatabase();
        if (!_completed)
        {
            TryDeletePartial(_partialPath);
        }
    }

    private static readonly byte[] ZeroHash = new byte[32];

    private SqliteCommand Prepare(string commandText, params (string Name, SqliteType Type)[] parameters)
    {
        SqliteCommand command = _database.Connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = commandText;
        foreach ((string name, SqliteType type) in parameters)
        {
            command.Parameters.Add(name, type);
        }

        command.Prepare();
        return command;
    }

    private static void SetParameter(SqliteCommand command, string name, object value)
    {
        command.Parameters[name].Value = value;
    }

    private void DisposeCommands()
    {
        if (_commandsDisposed)
        {
            return;
        }

        _commandsDisposed = true;
        TryDispose(_insertSheet);
        TryDispose(_insertColumn);
        TryDispose(_updateSheet);
        TryDispose(_insertRow);
        TryDispose(_insertStringCell);
        TryDispose(_insertMeta);
    }

    private void DisposeTransaction()
    {
        if (_transactionDisposed)
        {
            return;
        }

        _transaction.Dispose();
        _transactionDisposed = true;
    }

    private void RollbackAndDisposeTransactionSafely()
    {
        if (_transactionDisposed)
        {
            return;
        }

        try
        {
            _transaction.Rollback();
        }
        catch
        {
            // Continue best-effort cleanup without hiding the original failure.
        }

        TryDispose(_transaction);
        _transactionDisposed = true;
    }

    private void DisposeDatabase()
    {
        if (_databaseDisposed)
        {
            return;
        }

        _databaseDisposed = true;
        TryDispose(_database);
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch
        {
            // Cleanup must not hide an extraction or write failure.
        }
    }

    private static void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // An orphaned partial is preferable to hiding the original failure.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void FlushToDisk(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        stream.Flush(true);
    }
}

public static class LuminaVersion
{
    public const string Current = "7.7.0";
}
