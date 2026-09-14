using Microsoft.Data.Sqlite;
using System.Text.Json;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Tests;

public sealed class HxsPipelineTests
{
    [Fact]
    public void SchemaAndIdentificationAreCreated()
    {
        string path = NewPath();
        try
        {
            new HxsWriter().WriteEmpty(path);
            using SqliteConnection connection = Open(path);
            Assert.Equal(HxsConstants.ApplicationId, PragmaInt(connection, "application_id"));
            Assert.Equal(HxsFormatVersion.Current, PragmaInt(connection, "user_version"));
            foreach (string table in new[] { "hxs_meta", "sheets", "columns", "rows", "string_cells" })
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                command.Parameters.AddWithValue("$name", table);
                Assert.Equal(1L, (long)command.ExecuteScalar()!);
            }
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void DefaultRowCoordinatesArePersisted()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            using SqliteConnection connection = Open(path);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT row_id, subrow_id FROM \"rows\";";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(42L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SubrowCoordinatesArePersisted()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.Subrows, 7);
            using SqliteConnection connection = Open(path);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT row_id, subrow_id FROM \"rows\";";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(42L, reader.GetInt64(0));
            Assert.Equal(7L, reader.GetInt64(1));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void TechnicalPayloadUsesCanonicalFraming()
    {
        HxsTechnicalCell cell = new(3, HarmoniaColumnType.UInt16, new byte[] { 0x34, 0x12 });
        byte[] payload = HxsHashing.EncodeTechnicalPayload([cell]);

        Assert.Equal(
            new byte[] { 3, 0, 0, 0, 13, 0, 0, 0, 2, 0, 0, 0, 0x34, 0x12 },
            payload);
    }

    [Fact]
    public void SignedUnsignedAndFloatValuesPreserveCanonicalBits()
    {
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x80 }, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Int32, int.MinValue));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.UInt32, uint.MaxValue));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0x7F }, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Float32, float.PositiveInfinity));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0xFF }, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Float32, float.NegativeInfinity));
    }

    [Fact]
    public void NegativeZeroAndPositiveZeroHashDiffer()
    {
        HxsTechnicalCell positive = new(0, HarmoniaColumnType.Float32, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Float32, 0.0f));
        HxsTechnicalCell negative = new(0, HarmoniaColumnType.Float32, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Float32, -0.0f));

        Assert.NotEqual(
            Convert.ToHexString(HxsHashing.HashRowTechnical("Sheet", 1, 0, [positive])),
            Convert.ToHexString(HxsHashing.HashRowTechnical("Sheet", 1, 0, [negative])));
    }

    [Fact]
    public void MacroAndRawStringFramingIsDeterministicAndUnambiguous()
    {
        Assert.Equal(HxsHashing.HashMacro("hello"), HxsHashing.HashMacro("hello"));
        HxsStringCellRecord withoutRaw = new(1, 0, 0, "hello", null, HxsHashing.HashMacro("hello"), null);
        HxsStringCellRecord withEmptyRaw = new(1, 0, 0, "hello", Array.Empty<byte>(), HxsHashing.HashMacro("hello"), HxsHashing.HashRaw(Array.Empty<byte>()));

        Assert.NotEqual(
            Convert.ToHexString(HxsHashing.HashRowStrings("Sheet", 1, 0, [withoutRaw])),
            Convert.ToHexString(HxsHashing.HashRowStrings("Sheet", 1, 0, [withEmptyRaw])));
    }

    [Fact]
    public void ChangingTechnicalStringOrSchemaDataChangesExpectedHashes()
    {
        string pathA = NewPath();
        string pathB = NewPath();
        try
        {
            HxsMetadata first = WriteSynthetic(pathA, HarmoniaSheetVariant.DefaultRows, 0, 10, "one");
            HxsMetadata technicalChange = WriteSynthetic(pathB, HarmoniaSheetVariant.DefaultRows, 0, 11, "one");
            Assert.NotEqual(first.ContentId, technicalChange.ContentId);

            string pathC = NewPath();
            try
            {
                HxsMetadata stringChange = WriteSynthetic(pathC, HarmoniaSheetVariant.DefaultRows, 0, 10, "two");
                Assert.NotEqual(first.ContentId, stringChange.ContentId);
            }
            finally
            {
                Delete(pathC);
            }
        }
        finally
        {
            Delete(pathA);
            Delete(pathB);
        }
    }

    [Fact]
    public void ContentIdIgnoresGameVersionButSnapshotIdIncludesIt()
    {
        HxsSheetRecord sheet = SyntheticSheet(10, "one", "game");
        HxsSheetRecord sameContent = SyntheticSheet(10, "one", "other");
        string firstContent = HxsHashing.ComputeContentId("en", [sheet]);
        string secondContent = HxsHashing.ComputeContentId("en", [sameContent]);

        Assert.Equal(firstContent, secondContent);
        Assert.NotEqual(HxsHashing.ComputeSnapshotId("game", "en", firstContent), HxsHashing.ComputeSnapshotId("other", "en", firstContent));
    }

    [Fact]
    public void SheetOrderingDoesNotChangeContentId()
    {
        HxsSheetRecord first = SyntheticSheet(1, "one", "game", "A");
        HxsSheetRecord second = SyntheticSheet(2, "two", "game", "B");

        Assert.Equal(
            HxsHashing.ComputeContentId("en", [first, second]),
            HxsHashing.ComputeContentId("en", [second, first]));
    }

    [Fact]
    public void ContentIdIncludesEffectiveLanguageAndIsDeterministic()
    {
        HxsSheetRecord sheet = SyntheticSheet(1, "one", "game");
        HxsSheetRecord english = sheet with { EffectiveLanguage = "en" };
        HxsSheetRecord japanese = sheet with { EffectiveLanguage = "ja" };

        string first = HxsHashing.ComputeContentId("en", [english]);
        string repeated = HxsHashing.ComputeContentId("en", [english]);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, HxsHashing.ComputeContentId("en", [japanese]));
    }

    [Fact]
    public void VerifyAcceptsGeneratedSyntheticArtifact()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            HxsVerificationResult result = HxsVerifier.Verify(path);
            Assert.True(result.IntegrityCheckPassed);
            Assert.Equal(1, result.Metadata.SheetCount);
            Assert.Equal(1, result.Metadata.RowCount);
            Assert.Equal(1, result.Metadata.StringCellCount);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsExtraUserTable()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "CREATE TABLE extra_user_table (id INTEGER);");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsExtraView()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "CREATE VIEW extra_user_view AS SELECT name FROM sheets;");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsExtraTrigger()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "CREATE TRIGGER extra_user_trigger AFTER INSERT ON sheets BEGIN SELECT 1; END;");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsExtraUserIndex()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "CREATE INDEX extra_index ON sheets(name);");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsExtraColumn()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "ALTER TABLE sheets ADD COLUMN unexpected TEXT;");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsGeneratedColumn()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "ALTER TABLE sheets ADD COLUMN generated_name TEXT GENERATED ALWAYS AS (name) VIRTUAL;");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsTamperedEffectiveLanguage()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(path, "UPDATE sheets SET effective_language = 'ja' WHERE name = 'Synthetic';");

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VerifyRejectsTamperedRowDataAndHashes()
    {
        string dataPath = NewPath();
        string hashPath = NewPath();
        try
        {
            WriteSynthetic(dataPath, HarmoniaSheetVariant.DefaultRows, 0);
            WriteSynthetic(hashPath, HarmoniaSheetVariant.DefaultRows, 0);
            Tamper(dataPath, "UPDATE \"rows\" SET technical_payload = X'00' WHERE row_id = 42;");
            Tamper(hashPath, "UPDATE \"rows\" SET row_hash = X'00' WHERE row_id = 42;");
            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(dataPath));
            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(hashPath));
        }
        finally
        {
            Delete(dataPath);
            Delete(hashPath);
        }
    }

    [Fact]
    public void InspectJsonIsParseableAndStable()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            string json = HxsInspector.Inspect(path).ToJson();
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal(1, document.RootElement.GetProperty("hxsVersion").GetInt32());
            Assert.Equal("en", document.RootElement.GetProperty("language").GetString());
            Assert.Equal("full", document.RootElement.GetProperty("scope").GetString());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void FailedExtractionDoesNotCreateFinalOutput()
    {
        string path = NewPath();
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => new ExtractionEngine().Extract(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "en", path));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Delete(path);
            Delete(path + ".partial");
        }
    }

    [Fact]
    public void FailedWriteSessionRemovesPartialOutput()
    {
        string path = NewPath();
        try
        {
            using (HxsWriteSession session = new HxsWriter().Begin(path))
            {
                Assert.True(File.Exists(path + ".partial"));
                Assert.Throws<InvalidOperationException>(() => session.Complete());
            }

            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExistingFinalOutputIsNotOverwritten()
    {
        string path = NewPath();
        try
        {
            WriteSynthetic(path, HarmoniaSheetVariant.DefaultRows, 0);
            byte[] before = File.ReadAllBytes(path);
            Assert.Throws<IOException>(() => new HxsWriter().WriteEmpty(path));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            Delete(path);
        }
    }

    private static HxsMetadata WriteSynthetic(
        string path,
        HarmoniaSheetVariant variant,
        ushort subrowId,
        int technicalValue = 10,
        string stringValue = "one")
    {
        HxsSheetRecord sheet = SyntheticSheet(technicalValue, stringValue, "game", "Synthetic", variant, subrowId);
        string contentId = HxsHashing.ComputeContentId("en", [sheet]);
        HxsMetadata metadata = new(
            1,
            "game",
            "en",
            "full",
            contentId,
            HxsHashing.ComputeSnapshotId("game", "en", contentId),
            "test",
            "7.7.0",
            1,
            1,
            1);

        using HxsWriteSession session = new HxsWriter().Begin(path);
        int sheetId = session.BeginSheet(sheet);
        HxsTechnicalCell technicalCell = new(1, HarmoniaColumnType.Int32, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Int32, technicalValue));
        HxsStringCellRecord stringCell = new(42, subrowId, 0, stringValue, null, HxsHashing.HashMacro(stringValue), null);
        byte[] payload = HxsHashing.EncodeTechnicalPayload([technicalCell]);
        byte[] technicalHash = HxsHashing.HashRowTechnical("Synthetic", 42, subrowId, [technicalCell]);
        byte[] stringHash = HxsHashing.HashRowStrings("Synthetic", 42, subrowId, [stringCell]);
        HxsRowRecord row = new(42, subrowId, payload, HxsHashing.HashRow("Synthetic", 42, subrowId, technicalHash, stringHash), technicalHash, stringHash, [stringCell]);
        session.WriteRow(sheetId, row);
        session.CompleteSheet(sheetId, sheet);
        session.WriteMetadata(metadata);
        session.Complete();
        return metadata;
    }

    private static HxsSheetRecord SyntheticSheet(
        int technicalValue,
        string stringValue,
        string gameVersion,
        string sheetName = "Synthetic",
        HarmoniaSheetVariant variant = HarmoniaSheetVariant.DefaultRows,
        ushort subrowId = 0)
    {
        HarmoniaColumnDefinition[] columns =
        [
            new(0, 0, HarmoniaColumnType.String),
            new(1, 4, HarmoniaColumnType.Int32),
        ];
        byte[] schemaHash = HxsHashing.HashSchema(sheetName, variant, columns);
        HxsTechnicalCell technicalCell = new(1, HarmoniaColumnType.Int32, HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Int32, technicalValue));
        HxsStringCellRecord stringCell = new(42, subrowId, 0, stringValue, null, HxsHashing.HashMacro(stringValue), null);
        HxsRowRecord row = new(
            42,
            subrowId,
            HxsHashing.EncodeTechnicalPayload([technicalCell]),
            HxsHashing.HashRow(sheetName, 42, subrowId, HxsHashing.HashRowTechnical(sheetName, 42, subrowId, [technicalCell]), HxsHashing.HashRowStrings(sheetName, 42, subrowId, [stringCell])),
            HxsHashing.HashRowTechnical(sheetName, 42, subrowId, [technicalCell]),
            HxsHashing.HashRowStrings(sheetName, 42, subrowId, [stringCell]),
            [stringCell]);
        HxsSheetHashAccumulator accumulator = new(sheetName);
        accumulator.AddRow(row);
        byte[] technicalHash = accumulator.ComputeTechnicalHash();
        byte[] stringHash = accumulator.ComputeStringHash();
        return new HxsSheetRecord(
            sheetName,
            variant,
            "en",
            columns,
            1,
            schemaHash,
            technicalHash,
            stringHash,
            HxsHashing.HashSheetContent(sheetName, variant, schemaHash, technicalHash, stringHash));
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

    private static long PragmaInt(SqliteConnection connection, string name)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return (long)command.ExecuteScalar()!;
    }

    private static void Tamper(string path, string sql)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"harmonia-atlas-test-{Guid.NewGuid():N}.hxs");

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (File.Exists(path + ".partial"))
        {
            File.Delete(path + ".partial");
        }
    }
}
