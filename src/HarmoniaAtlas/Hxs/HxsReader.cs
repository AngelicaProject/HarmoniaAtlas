namespace HarmoniaAtlas.Hxs;

public sealed class HxsReader : IDisposable
{
    private readonly HxsDatabase _database;

    private HxsReader(HxsDatabase database)
    {
        _database = database;
    }

    internal HxsDatabase Database => _database;

    public int ApplicationId => _database.ApplicationId;

    public int FormatVersion => _database.UserVersion;

    public HxsMetadata Metadata => HxsQueries.ReadMetadata(_database.Connection);

    public IReadOnlyList<HxsSheetRecord> ReadSheets() => HxsQueries.ReadSheets(_database.Connection);

    public IEnumerable<HxsStringRowRecord> ReadStringRows(string sheetName) =>
        HxsQueries.ReadStringRows(_database.Connection, sheetName);

    public static HxsReader Open(string path) => OpenReadOnly(path);

    public static HxsReader OpenReadOnly(string path)
    {
        HxsDatabase database = HxsDatabase.OpenReadOnly(path);
        try
        {
            ValidateIdentity(database);
            return new HxsReader(database);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    public bool RunIntegrityCheck() => _database.RunIntegrityCheck();

    public void Dispose() => _database.Dispose();

    private static void ValidateIdentity(HxsDatabase database)
    {
        if (database.ApplicationId != HxsConstants.ApplicationId)
        {
            throw new HxsFormatException(
                $"Invalid HXS marker. Expected application_id {HxsConstants.ApplicationId}, got {database.ApplicationId}.");
        }

        if (database.UserVersion != HxsFormatVersion.Current)
        {
            throw new HxsFormatException(
                $"Unsupported HXS format version {database.UserVersion}. Expected {HxsFormatVersion.Current}.");
        }
    }
}
