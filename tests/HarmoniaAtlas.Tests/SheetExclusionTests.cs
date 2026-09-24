using Microsoft.Data.Sqlite;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Game;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Tests;

public sealed class SheetExclusionTests
{
    [Fact]
    public void UnreadableSheetsAreExcludedAndRolledBackWhileOtherSheetsAreStored()
    {
        string path = NewPath();
        try
        {
            FakeSource source = new(
                new FakeSheet("Good", ["Hello", "World"]),
                new FakeSheet("Midway", ["kept?", "never"], FailAfterRows: 1),
                new FakeSheet("Unsupported", ["x"], OpenFailure: HxsSheetExclusionReason.UnsupportedColumnType));

            ExtractionSummary summary = new ExtractionEngine().Extract(source, "game", "en", path);

            Assert.Equal(1, summary.SheetCount);
            Assert.Equal(2, summary.RowCount);
            Assert.Equal(
                [
                    new HxsExcludedSheet("Midway", HxsSheetExclusionReason.UnreadableData),
                    new HxsExcludedSheet("Unsupported", HxsSheetExclusionReason.UnsupportedColumnType),
                ],
                summary.ExcludedSheets);

            HxsVerificationResult verification = HxsVerifier.Verify(path);
            Assert.Equal(2, verification.Metadata.ExcludedSheetCount);
            Assert.Equal(summary.ContentId, verification.Metadata.ContentId);
            using HxsReader reader = HxsReader.OpenReadOnly(path);
            Assert.Equal(["Good"], reader.ReadSheets().Select(sheet => sheet.Name));
            Assert.Equal(summary.ExcludedSheets, reader.ReadExcludedSheets());
            using SqliteConnection connection = Open(path);
            Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM \"rows\";"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExclusionsAreDeterministicAndPartOfTheContentId()
    {
        string first = NewPath();
        string second = NewPath();
        string withoutExclusion = NewPath();
        try
        {
            FakeSheet good = new("Good", ["Hello"]);
            string firstId = new ExtractionEngine().Extract(
                new FakeSource(good, new FakeSheet("Broken", ["x"], FailAfterRows: 0)), "game", "en", first).ContentId;
            string secondId = new ExtractionEngine().Extract(
                new FakeSource(new FakeSheet("Broken", ["x"], FailAfterRows: 0), good), "game", "en", second).ContentId;
            string plainId = new ExtractionEngine().Extract(new FakeSource(good), "game", "en", withoutExclusion).ContentId;

            Assert.Equal(firstId, secondId);
            Assert.NotEqual(firstId, plainId);
        }
        finally
        {
            Delete(first);
            Delete(second);
            Delete(withoutExclusion);
        }
    }

    [Fact]
    public void ACatalogWithoutAnyReadableSheetFailsWithoutOutput()
    {
        string path = NewPath();
        try
        {
            FakeSource source = new(new FakeSheet("Broken", ["x"], OpenFailure: HxsSheetExclusionReason.UnreadableData));
            Assert.Throws<InvalidDataException>(() => new ExtractionEngine().Extract(source, "game", "en", path));
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsTamperedOrOverlappingExclusions()
    {
        string path = NewPath();
        try
        {
            new ExtractionEngine().Extract(
                new FakeSource(new FakeSheet("Good", ["Hello"]), new FakeSheet("Broken", ["x"], FailAfterRows: 0)),
                "game",
                "en",
                path);

            Tamper(path, "UPDATE excluded_sheets SET reason = 1;");
            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
            Tamper(path, "UPDATE excluded_sheets SET reason = 3;");
            HxsVerifier.Verify(path);

            Tamper(path, "UPDATE excluded_sheets SET name = 'Good';");
            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
            Tamper(path, "UPDATE excluded_sheets SET name = 'Broken';");

            Tamper(path, "DELETE FROM excluded_sheets;");
            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void GuidanceMarksSheetsUnreadableInAnyInputAsIncompatible()
    {
        string en = NewPath();
        string ja = NewPath();
        try
        {
            new ExtractionEngine().Extract(
                new FakeSource(new FakeSheet("Item", ["Potion"]), new FakeSheet("Quest", ["Hello"])),
                "game",
                "en",
                en);
            new ExtractionEngine().Extract(
                new FakeSource(new FakeSheet("Item", ["ポーション"]), new FakeSheet("Quest", ["x"], FailAfterRows: 0)),
                "game",
                "ja",
                ja);

            SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(en, [ja]);

            SourceGuidanceSheet item = bundle.Sheets.Single(sheet => sheet.Name == "Item");
            SourceGuidanceSheet quest = bundle.Sheets.Single(sheet => sheet.Name == "Quest");
            Assert.Equal(SourceGuidanceSheetStatus.Compatible, item.Status);
            Assert.Single(item.Translatable);
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, quest.Status);
            Assert.Equal([SourceGuidanceIncompatibilityReason.UnreadableInInput], quest.IncompatibilityReasons);
            Assert.Empty(quest.Translatable);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void GuidanceMarksASheetThatFailsWhileBeingComparedAsUnreadable()
    {
        string en = NewPath();
        string ja = NewPath();
        try
        {
            new ExtractionEngine().Extract(new FakeSource(new FakeSheet("Item", ["Potion", "Ether"])), "game", "en", en);
            new ExtractionEngine().Extract(new FakeSource(new FakeSheet("Item", ["ポーション", "エーテル"])), "game", "ja", ja);
            using HxsGuidanceEvidenceSource source = HxsGuidanceEvidenceSource.OpenVerified(en);
            using HxsGuidanceEvidenceSource comparison = HxsGuidanceEvidenceSource.OpenVerified(ja);
            using FailingEvidenceSource failing = new(comparison, failAfterRows: 1);

            SourceGuidanceBundle bundle = new SourceGuidanceAnalyzer().Analyze(source, [failing]);

            SourceGuidanceSheet item = bundle.Sheets.Single();
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, item.Status);
            Assert.Equal([SourceGuidanceIncompatibilityReason.UnreadableInInput], item.IncompatibilityReasons);
            Assert.Empty(item.Translatable);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    private sealed record FakeSheet(
        string Name,
        IReadOnlyList<string> Values,
        HxsSheetExclusionReason? OpenFailure = null,
        int? FailAfterRows = null);

    private sealed class FakeSource : IExtractionSource
    {
        private readonly Dictionary<string, FakeSheet> _sheets;

        public FakeSource(params FakeSheet[] sheets)
        {
            _sheets = sheets.ToDictionary(sheet => sheet.Name, StringComparer.Ordinal);
        }

        public IReadOnlyList<string> SheetNames => _sheets.Keys.ToArray();

        public IExtractionSheet OpenSheet(string sheetName)
        {
            FakeSheet sheet = _sheets[sheetName];
            if (sheet.OpenFailure is { } reason)
            {
                throw new SheetReadException(sheetName, reason, "synthetic open failure");
            }

            return new FakeExtractionSheet(sheet);
        }
    }

    private sealed class FakeExtractionSheet(FakeSheet sheet) : IExtractionSheet
    {
        public HarmoniaSheetInfo Info { get; } = new(
            sheet.Name,
            HarmoniaSheetVariant.DefaultRows,
            "none",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String), new HarmoniaColumnDefinition(1, 4, HarmoniaColumnType.Int32)]);

        public IEnumerable<HarmoniaRowData> EnumerateRows()
        {
            for (int index = 0; index < sheet.Values.Count; index++)
            {
                if (sheet.FailAfterRows == index)
                {
                    throw new IOException("synthetic unreadable page");
                }

                yield return new HarmoniaRowData(
                    (uint)index + 1,
                    0,
                    [HarmoniaCellData.String(0, sheet.Values[index], null), HarmoniaCellData.Technical(1, HarmoniaColumnType.Int32, index)]);
            }
        }
    }

    private sealed class FailingEvidenceSource(IGuidanceEvidenceSource inner, int failAfterRows) : IGuidanceEvidenceSource
    {
        public string Language => inner.Language;
        public string GameVersion => inner.GameVersion;
        public string Scope => inner.Scope;
        public string? ContentId => inner.ContentId;
        public string? SnapshotId => inner.SnapshotId;
        public IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets => inner.Sheets;

        public IEnumerable<GuidanceStringRow> ReadStringRows(string sheetName)
        {
            int rows = 0;
            foreach (GuidanceStringRow row in inner.ReadStringRows(sheetName))
            {
                if (rows++ == failAfterRows)
                {
                    throw new SheetReadException(sheetName, HxsSheetExclusionReason.UnreadableData, "synthetic read failure");
                }

                yield return row;
            }
        }

        public void Dispose()
        {
        }
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"harmonia-atlas-exclusion-{Guid.NewGuid():N}.hxs");

    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string candidate in new[] { path, path + ".partial" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void Tamper(string path, string sql)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
