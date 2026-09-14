using Microsoft.Data.Sqlite;

namespace HarmoniaAtlas.Hxs;

public sealed class HxsDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    private HxsDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static HxsDatabase CreateNew(string path)
    {
        string fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new IOException($"SQLite file already exists: {fullPath}");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        return OpenConnection(fullPath, SqliteOpenMode.ReadWriteCreate);
    }

    public static HxsDatabase OpenExisting(string path)
    {
        string fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SQLite file was not found.", fullPath);
        }

        return OpenConnection(fullPath, SqliteOpenMode.ReadWrite);
    }

    public static HxsDatabase OpenReadOnly(string path)
    {
        string fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SQLite file was not found.", fullPath);
        }

        return OpenConnection(fullPath, SqliteOpenMode.ReadOnly);
    }

    internal SqliteConnection Connection
    {
        get
        {
            ThrowIfDisposed();
            return _connection;
        }
    }

    public int ApplicationId => ReadPragmaInt32("application_id");

    public int UserVersion => ReadPragmaInt32("user_version");

    public void SetHxsIdentity()
    {
        SetApplicationId(HxsConstants.ApplicationId);
        SetUserVersion(HxsFormatVersion.Current);
    }

    public void SetApplicationId(int applicationId)
    {
        ExecutePragmaAssignment("application_id", applicationId);
    }

    public void SetUserVersion(int userVersion)
    {
        ExecutePragmaAssignment("user_version", userVersion);
    }

    public bool RunIntegrityCheck()
    {
        using SqliteCommand command = CreateCommand("PRAGMA integrity_check;");
        string? result = command.ExecuteScalar()?.ToString();
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    public bool RunForeignKeyCheck()
    {
        using SqliteCommand command = CreateCommand("PRAGMA foreign_key_check;");
        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read();
    }

    public SqliteTransaction BeginTransaction()
    {
        ThrowIfDisposed();
        return _connection.BeginTransaction();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private static HxsDatabase OpenConnection(string fullPath, SqliteOpenMode mode)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = fullPath,
            Mode = mode,
            Pooling = false,
        };
        SqliteConnection connection = new(connectionString.ToString());
        try
        {
            connection.Open();
            HxsDatabase database = new(connection);
            database.EnableForeignKeys();
            if (mode == SqliteOpenMode.ReadWriteCreate)
            {
                database.SetJournalModeDelete();
            }

            return database;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private int ReadPragmaInt32(string pragmaName)
    {
        ThrowIfDisposed();
        using SqliteCommand command = CreateCommand($"PRAGMA {pragmaName};");
        object? value = command.ExecuteScalar();
        return checked(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ExecutePragmaAssignment(string pragmaName, int value)
    {
        ThrowIfDisposed();
        using SqliteCommand command = CreateCommand($"PRAGMA {pragmaName} = {value};");
        command.ExecuteNonQuery();
    }

    internal SqliteCommand CreateCommand(string text)
    {
        ThrowIfDisposed();
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = text;
        return command;
    }

    private void EnableForeignKeys()
    {
        using SqliteCommand command = CreateCommand("PRAGMA foreign_keys = ON;");
        command.ExecuteNonQuery();
    }

    private void SetJournalModeDelete()
    {
        using SqliteCommand command = CreateCommand("PRAGMA journal_mode = DELETE;");
        command.ExecuteScalar();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
