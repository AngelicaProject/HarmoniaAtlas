using System.Text.Json;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;
using Microsoft.Data.Sqlite;

namespace HarmoniaAtlas.Tests;

public sealed class SourceGuidanceTests
{
    [Fact]
    public void GuidanceRequiresAtLeastTwoInputs()
    {
        string path = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([path]));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void GuidanceRejectsDuplicateLanguageAndMismatchedMetadata()
    {
        string duplicateEn = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string duplicateEn2 = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string gameEn = CreateSnapshot("en", [Sheet("Item", ["Fire Shard"])], gameVersion: "2026.09");
        string gameJa = CreateSnapshot("ja", [Sheet("Item", ["ファイアシャード"])], gameVersion: "2026.08");
        string scopeEn = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string scopeJa = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        SetScope(scopeJa, "partial");
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([duplicateEn, duplicateEn2]));
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([gameEn, gameJa]));
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([scopeEn, scopeJa]));
        }
        finally
        {
            Delete(duplicateEn);
            Delete(duplicateEn2);
            Delete(gameEn);
            Delete(gameJa);
            Delete(scopeEn);
            Delete(scopeJa);
        }
    }

    [Fact]
    public void GuidanceRejectsInvalidHxs()
    {
        string invalid = NewPath();
        string valid = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        File.WriteAllText(invalid, "not an HXS database");
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([invalid, valid]));
        }
        finally
        {
            Delete(invalid);
            Delete(valid);
        }
    }

    [Theory]
    [InlineData("pirate")]
    [InlineData("english")]
    [InlineData("zh_CN")]
    public void GuidanceRejectsNonCanonicalSourceLanguage(string language)
    {
        string invalidLanguage = CreateSnapshot(language, Sheet("Item", ["Fire Shard"]));
        string valid = CreateSnapshot("en", Sheet("Item", ["ファイアシャード"]));
        try
        {
            Assert.Throws<SourceGuidanceException>(() => Analyze([invalidLanguage, valid]));
        }
        finally
        {
            Delete(invalidLanguage);
            Delete(valid);
        }
    }

    [Fact]
    public void ExactVarianceAddsOnlyThatOccurrenceToTheAllowlist()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        string de = CreateSnapshot("de", Sheet("Item", ["Feuerscherbe"]));
        string fr = CreateSnapshot("fr", Sheet("Item", ["Éclat de feu"]));
        try
        {
            SourceGuidanceSheet sheet = Analyze([en, ja, de, fr]).Sheets.Single();
            SourceGuidanceOccurrence occurrence = Assert.Single(sheet.Translatable);

            Assert.Equal(new SourceGuidanceOccurrence(1, 0, 0), occurrence);
            Assert.Equal(SourceGuidanceSheetStatus.Compatible, sheet.Status);
        }
        finally
        {
            Delete(en);
            Delete(ja);
            Delete(de);
            Delete(fr);
        }
    }

    [Fact]
    public void InvariantTechnicalLookingOccurrenceIsAbsent()
    {
        string en = CreateSnapshot("en", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string ja = CreateSnapshot("ja", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string de = CreateSnapshot("de", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string fr = CreateSnapshot("fr", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        try
        {
            SourceGuidanceSheet sheet = Analyze([en, ja, de, fr]).Sheets.Single();

            Assert.Empty(sheet.Translatable);
        }
        finally
        {
            Delete(en);
            Delete(ja);
            Delete(de);
            Delete(fr);
        }
    }

    [Fact]
    public void MixedUseColumnAllowsOnlyTheVaryingRow()
    {
        string en = CreateSnapshot("en", Sheet("Mixed", ["LogChatBubbleShoutFontColor"], ["Hello"]));
        string ja = CreateSnapshot("ja", Sheet("Mixed", ["LogChatBubbleShoutFontColor"], ["こんにちは"]));
        string de = CreateSnapshot("de", Sheet("Mixed", ["LogChatBubbleShoutFontColor"], ["Hallo"]));
        string fr = CreateSnapshot("fr", Sheet("Mixed", ["LogChatBubbleShoutFontColor"], ["Bonjour"]));
        try
        {
            SourceGuidanceSheet sheet = Analyze([en, ja, de, fr]).Sheets.Single();
            SourceGuidanceOccurrence occurrence = Assert.Single(sheet.Translatable);

            Assert.Equal(2u, occurrence.RowId);
            Assert.Equal((ushort)0, occurrence.SubrowId);
            Assert.Equal(0, occurrence.ColumnIndex);
        }
        finally
        {
            Delete(en);
            Delete(ja);
            Delete(de);
            Delete(fr);
        }
    }

    [Fact]
    public void QuestLikeInvariantIdentifierIsAbsentWhileDialogueIsAllowed()
    {
        string en = CreateSnapshot("en", Sheet("Quest", ["TEXT_QUEST_001", "Greetings and welcome"]));
        string ja = CreateSnapshot("ja", Sheet("Quest", ["TEXT_QUEST_001", "こんにちは"]));
        try
        {
            SourceGuidanceSheet sheet = Analyze([en, ja]).Sheets.Single();
            SourceGuidanceOccurrence occurrence = Assert.Single(sheet.Translatable);

            Assert.Equal(1, occurrence.ColumnIndex);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void ItemLikeVaryingStringCellsAreIndependentlyAllowed()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard", "A small shard", "Fire Shard"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード", "小さな欠片", "ファイアシャード"]));
        try
        {
            SourceGuidanceSheet sheet = Analyze([en, ja]).Sheets.Single();

            Assert.Equal([0, 1, 2], sheet.Translatable.Select(occurrence => occurrence.ColumnIndex));
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void EmptyAndNonEmptyValuesCountAsVariance()
    {
        string en = CreateSnapshot("en", Sheet("Item", [""]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["こんにちは"]));
        try
        {
            Assert.Equal(new SourceGuidanceOccurrence(1, 0, 0), Assert.Single(Analyze([en, ja]).Sheets.Single().Translatable));
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void RawValueDifferencesAloneDoNotCreatePermission()
    {
        string en = CreateSnapshot("en", new SheetSpec(
            "Item",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            [new RowSpec(["same"], RawValues: new byte[]?[] { [1] })]));
        string ja = CreateSnapshot("ja", new SheetSpec(
            "Item",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            [new RowSpec(["same"], RawValues: new byte[]?[] { [2] })]));
        try
        {
            Assert.Empty(Analyze([en, ja]).Sheets.Single().Translatable);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void SchemaAndTopologyMismatchesFailClosedWithEmptyAllowlists()
    {
        string schemaEn = CreateSnapshot("en", Sheet("Item", ["one"]));
        string schemaJa = CreateSnapshot(
            "ja",
            new SheetSpec(
                "Item",
                [
                    new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String),
                    new HarmoniaColumnDefinition(1, 4, HarmoniaColumnType.String),
                ],
                [new RowSpec(["uno", "dos"])]) );
        string topologyEn = CreateSnapshot("en", Sheet("Quest", ["one"]));
        string topologyJa = CreateSnapshot("ja", Sheet("Quest", ["uno"], ["dos"]));
        try
        {
            SourceGuidanceSheet schemaSheet = Analyze([schemaEn, schemaJa]).Sheets.Single();
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, schemaSheet.Status);
            Assert.Empty(schemaSheet.Translatable);
            Assert.Contains(SourceGuidanceIncompatibilityReason.ColumnDefinitionMismatch, schemaSheet.IncompatibilityReasons);

            SourceGuidanceSheet topologySheet = Analyze([topologyEn, topologyJa]).Sheets.Single();
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, topologySheet.Status);
            Assert.Empty(topologySheet.Translatable);
            Assert.Contains(SourceGuidanceIncompatibilityReason.RowTopologyMismatch, topologySheet.IncompatibilityReasons);
        }
        finally
        {
            Delete(schemaEn);
            Delete(schemaJa);
            Delete(topologyEn);
            Delete(topologyJa);
        }
    }

    [Fact]
    public void MissingSheetIsEmittedAsIncompatibleWithEmptyAllowlist()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]), Sheet("OnlyEnglish", ["Technical"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        try
        {
            SourceGuidanceSheet missing = Analyze([en, ja]).Sheets.Single(sheet => sheet.Name == "OnlyEnglish");

            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, missing.Status);
            Assert.Empty(missing.Translatable);
            Assert.Contains(SourceGuidanceIncompatibilityReason.MissingInInput, missing.IncompatibilityReasons);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void GuidanceIsDeterministicAndReaderRejectsTamperedCoordinates()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        string de = CreateSnapshot("de", Sheet("Item", ["Feuerscherbe"]));
        string fr = CreateSnapshot("fr", Sheet("Item", ["Éclat de feu"]));
        string firstOutput = NewPath(".hsg.json");
        string secondOutput = NewPath(".hsg.json");
        try
        {
            new SourceGuidanceGenerator().Generate([en, ja, de, fr], firstOutput);
            new SourceGuidanceGenerator().Generate([fr, de, en, ja], secondOutput);

            byte[] firstBytes = File.ReadAllBytes(firstOutput);
            byte[] secondBytes = File.ReadAllBytes(secondOutput);
            Assert.Equal(firstBytes, secondBytes);
            Assert.False(firstBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Equal((byte)'\n', firstBytes[^1]);

            string firstJson = File.ReadAllText(firstOutput);
            Assert.DoesNotContain("\r", firstJson);
            Assert.Equal(1, firstJson.Count(character => character == '\n'));
            Assert.DoesNotContain("\n", firstJson[..^1]);

            SourceGuidanceBundle bundle = SourceGuidanceReader.Read(firstOutput);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(firstOutput));
            Assert.True(document.RootElement.TryGetProperty("sheets", out _));
            Assert.False(document.RootElement.TryGetProperty("semantics", out _));
            Assert.Equal(bundle.BundleId, SourceGuidanceReader.Read(secondOutput).BundleId);

            string tampered = File.ReadAllText(firstOutput).Replace("\"columnIndex\":0", "\"columnIndex\":1", StringComparison.Ordinal);
            File.WriteAllText(firstOutput, tampered);
            Assert.Throws<SourceGuidanceFormatException>(() => SourceGuidanceReader.Read(firstOutput));
        }
        finally
        {
            Delete(en);
            Delete(ja);
            Delete(de);
            Delete(fr);
            Delete(firstOutput);
            Delete(secondOutput);
        }
    }

    [Fact]
    public void GenerationFailureDoesNotPublishFinalOrPartialGuidance()
    {
        string invalid = NewPath();
        string valid = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string output = NewPath(".hsg.json");
        File.WriteAllText(invalid, "invalid");
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceGenerator().Generate([invalid, valid], output));
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(output + ".partial"));
        }
        finally
        {
            Delete(invalid);
            Delete(valid);
            Delete(output);
        }
    }

    private static SourceGuidanceBundle Analyze(IReadOnlyList<string> paths) => new SourceGuidanceAnalyzer().Analyze(paths);

    private static SheetSpec Sheet(string name, params string[][] rows) =>
        new(
            name,
            Enumerable.Range(0, rows.Select(values => values.Length).DefaultIfEmpty().Max())
                .Select(index => new HarmoniaColumnDefinition(index, index * 4, HarmoniaColumnType.String))
                .ToArray(),
            rows.Select(values => new RowSpec(values)).ToArray());

    private static string CreateSnapshot(string language, params SheetSpec[] sheets) =>
        CreateSnapshot(language, sheets, "game", "full");

    private static string CreateSnapshot(string language, SheetSpec[] sheets, string gameVersion = "game", string scope = "full")
    {
        string path = NewPath();
        List<(HxsSheetRecord Sheet, IReadOnlyList<HxsRowRecord> Rows)> builtSheets = sheets.Select(BuildSheet).ToList();
        using (HxsWriteSession session = new HxsWriter().Begin(path))
        {
            foreach ((HxsSheetRecord sheet, IReadOnlyList<HxsRowRecord> rows) in builtSheets)
            {
                int sheetId = session.BeginSheet(sheet);
                foreach (HxsRowRecord row in rows)
                {
                    session.WriteRow(sheetId, row);
                }

                session.CompleteSheet(sheetId, sheet);
            }

            string contentId = HxsHashing.ComputeContentId(language, builtSheets.Select(item => item.Sheet).ToArray());
            session.WriteMetadata(new HxsMetadata(
                1,
                gameVersion,
                language,
                scope,
                contentId,
                HxsHashing.ComputeSnapshotId(gameVersion, language, contentId),
                "test",
                "7.7.0",
                builtSheets.Count,
                builtSheets.Sum(item => (long)item.Rows.Count),
                builtSheets.Sum(item => (long)item.Rows.Sum(row => row.StringCells.Count))));
            session.Complete();
        }

        return path;
    }

    private static (HxsSheetRecord Sheet, IReadOnlyList<HxsRowRecord> Rows) BuildSheet(SheetSpec specification)
    {
        List<HxsRowRecord> rows = new();
        HxsSheetHashAccumulator accumulator = new(specification.Name);
        for (int index = 0; index < specification.Rows.Count; index++)
        {
            RowSpec sourceRow = specification.Rows[index];
            uint rowId = checked((uint)(index + 1));
            HxsStringCellRecord[] cells = sourceRow.Values
                .Select((value, columnIndex) =>
                {
                    byte[]? rawValue = sourceRow.RawValues is null ? null : sourceRow.RawValues[columnIndex];
                    return new HxsStringCellRecord(
                        rowId,
                        sourceRow.SubrowId,
                        columnIndex,
                        value,
                        rawValue,
                        HxsHashing.HashMacro(value),
                        rawValue is null ? null : HxsHashing.HashRaw(rawValue));
                })
                .ToArray();
            byte[] technicalHash = HxsHashing.HashRowTechnical(specification.Name, rowId, sourceRow.SubrowId, Array.Empty<HxsTechnicalCell>());
            byte[] stringHash = HxsHashing.HashRowStrings(specification.Name, rowId, sourceRow.SubrowId, cells);
            HxsRowRecord row = new(
                rowId,
                sourceRow.SubrowId,
                Array.Empty<byte>(),
                HxsHashing.HashRow(specification.Name, rowId, sourceRow.SubrowId, technicalHash, stringHash),
                technicalHash,
                stringHash,
                cells);
            rows.Add(row);
            accumulator.AddRow(row);
        }

        byte[] schemaHash = HxsHashing.HashSchema(specification.Name, HarmoniaSheetVariant.DefaultRows, specification.Columns);
        byte[] technicalSheetHash = accumulator.ComputeTechnicalHash();
        byte[] stringSheetHash = accumulator.ComputeStringHash();
        return (new HxsSheetRecord(
            specification.Name,
            HarmoniaSheetVariant.DefaultRows,
            "none",
            specification.Columns,
            rows.Count,
            schemaHash,
            technicalSheetHash,
            stringSheetHash,
            HxsHashing.HashSheetContent(specification.Name, HarmoniaSheetVariant.DefaultRows, schemaHash, technicalSheetHash, stringSheetHash)), rows);
    }

    private sealed record SheetSpec(string Name, IReadOnlyList<HarmoniaColumnDefinition> Columns, IReadOnlyList<RowSpec> Rows);

    private sealed record RowSpec(IReadOnlyList<string> Values, ushort SubrowId = 0, IReadOnlyList<byte[]?>? RawValues = null);

    private static string NewPath(string extension = ".hxs") =>
        Path.Combine(Path.GetTempPath(), $"harmonia-atlas-guidance-{Guid.NewGuid():N}{extension}");

    private static void SetScope(string path, string scope)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE hxs_meta SET scope = $scope WHERE id = 1;";
        command.Parameters.AddWithValue("$scope", scope);
        command.ExecuteNonQuery();
    }

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
