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
    public void GuidanceRejectsDuplicateLanguage()
    {
        string first = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string second = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([first, second]));
        }
        finally
        {
            Delete(first);
            Delete(second);
        }
    }

    [Fact]
    public void GuidanceRejectsDifferentGameVersionAndScope()
    {
        string gameA = CreateSnapshot("en", [Sheet("Item", ["Fire Shard"])], gameVersion: "2026.09");
        string gameB = CreateSnapshot("ja", [Sheet("Item", ["ファイアシャード"])], gameVersion: "2026.08");
        string scopeA = CreateSnapshot("en", [Sheet("Item", ["Fire Shard"])], scope: "full");
        string scopeB = CreateSnapshot("ja", [Sheet("Item", ["ファイアシャード"])], scope: "full");
        SetScope(scopeB, "partial");
        try
        {
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([gameA, gameB]));
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze([scopeA, scopeB]));
        }
        finally
        {
            Delete(gameA);
            Delete(gameB);
            Delete(scopeA);
            Delete(scopeB);
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

    [Fact]
    public void OfficialLanguageVarianceMakesAStringColumnTranslatable()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard", "Item description"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード", "アイテムの説明"]));
        string de = CreateSnapshot("de", Sheet("Item", ["Feuerscherbe", "Gegenstandsbeschreibung"]));
        string fr = CreateSnapshot("fr", Sheet("Item", ["Éclat de feu", "Description de l'objet"]));
        try
        {
            SourceGuidanceSheet item = Analyze([en, ja, de, fr]).Eligibility.Sheets.Single();

            Assert.Equal(SourceGuidanceRole.Translatable, item.Columns[0].Role);
            Assert.Equal(SourceGuidanceEvidenceKind.OfficialLanguageVariance, item.Columns[0].Evidence.Kind);
            Assert.Equal(1, item.Columns[0].Evidence.ComparableOccurrences);
            Assert.Equal(1, item.Columns[0].Evidence.VaryingOccurrences);
            Assert.Equal(SourceGuidanceRole.Translatable, item.Columns[1].Role);
            Assert.Equal(["de", "en", "fr", "ja"], item.Columns[0].Evidence.Languages);
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
    public void InvariantStringColumnRemainsUnknown()
    {
        string en = CreateSnapshot("en", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string ja = CreateSnapshot("ja", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string de = CreateSnapshot("de", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        string fr = CreateSnapshot("fr", Sheet("ChatBubbleType", ["LogChatBubbleShoutFontColor"]));
        try
        {
            SourceGuidanceColumn column = Analyze([en, ja, de, fr]).Eligibility.Sheets.Single().Columns.Single();

            Assert.Equal(SourceGuidanceRole.Unknown, column.Role);
            Assert.Equal(SourceGuidanceEvidenceKind.NoOfficialLanguageVariance, column.Evidence.Kind);
            Assert.Equal(0, column.Evidence.VaryingOccurrences);
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
    public void EmptyAndNonEmptyValuesCountAsVariance()
    {
        string en = CreateSnapshot("en", Sheet("Item", [""]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        try
        {
            SourceGuidanceColumn column = Analyze([en, ja]).Eligibility.Sheets.Single().Columns.Single();

            Assert.Equal(SourceGuidanceRole.Translatable, column.Role);
            Assert.Equal(1, column.Evidence.ComparableOccurrences);
            Assert.Equal(1, column.Evidence.VaryingOccurrences);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void RawValueDifferencesAloneDoNotCreateLocalizationEvidence()
    {
        string en = CreateSnapshot("en", new SheetSpec(
            "Item",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            [new RowSpec(["same"], 0, new byte[]?[] { [1] })]));
        string ja = CreateSnapshot("ja", new SheetSpec(
            "Item",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            [new RowSpec(["same"], 0, new byte[]?[] { [2] })]));
        try
        {
            SourceGuidanceColumn column = Analyze([en, ja]).Eligibility.Sheets.Single().Columns.Single();

            Assert.Equal(SourceGuidanceRole.Unknown, column.Role);
            Assert.Equal(SourceGuidanceEvidenceKind.NoOfficialLanguageVariance, column.Evidence.Kind);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void ExactTextNamespaceMakesAColumnContextOnlyWhenEveryNonEmptyValueMatches()
    {
        string en = CreateSnapshot("en", Sheet("Quest", ["TEXT_QUEST_001", "Hello"] , ["TEXT_QUEST_002", "Goodbye"]));
        string ja = CreateSnapshot("ja", Sheet("Quest", ["TEXT_QUEST_001", "こんにちは"], ["TEXT_QUEST_002", "さようなら"]));
        try
        {
            SourceGuidanceSheet quest = Analyze([en, ja]).Eligibility.Sheets.Single();

            Assert.Equal(SourceGuidanceRole.Context, quest.Columns[0].Role);
            Assert.Equal(SourceGuidanceEvidenceKind.KnownTechnicalNamespace, quest.Columns[0].Evidence.Kind);
            Assert.Equal("TEXT_", quest.Columns[0].Evidence.Prefix);
            Assert.Equal(SourceGuidanceRole.Translatable, quest.Columns[1].Role);
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }

        string mixedEn = CreateSnapshot("en", Sheet("Quest", ["TEXT_ABC"], ["Normal user-visible text"]));
        string mixedJa = CreateSnapshot("ja", Sheet("Quest", ["TEXT_ABC"], ["Normal user-visible text"]));
        try
        {
            SourceGuidanceColumn column = Analyze([mixedEn, mixedJa]).Eligibility.Sheets.Single().Columns.Single();
            Assert.Equal(SourceGuidanceRole.Unknown, column.Role);
            Assert.NotEqual(SourceGuidanceEvidenceKind.KnownTechnicalNamespace, column.Evidence.Kind);
        }
        finally
        {
            Delete(mixedEn);
            Delete(mixedJa);
        }
    }

    [Fact]
    public void SchemaAndTopologyMismatchesFailClosed()
    {
        string schemaEn = CreateSnapshot("en", Sheet("Item", ["one"]));
        string schemaJa = CreateSnapshot(
            "ja",
            [new SheetSpec(
                "Item",
                [
                    new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String),
                    new HarmoniaColumnDefinition(1, 4, HarmoniaColumnType.String),
                ],
                [new RowSpec(["uno", "two"])])]);
        string topologyEn = CreateSnapshot("en", Sheet("Quest", ["one"]));
        string topologyJa = CreateSnapshot("ja", Sheet("Quest", ["uno"], ["dos"]));
        try
        {
            SourceGuidanceSheet schemaSheet = Analyze([schemaEn, schemaJa]).Eligibility.Sheets.Single();
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, schemaSheet.Status);
            Assert.Contains(SourceGuidanceIncompatibilityReason.ColumnDefinitionMismatch, schemaSheet.IncompatibilityReasons);
            Assert.DoesNotContain(schemaSheet.Columns, column => column.Role == SourceGuidanceRole.Translatable);

            SourceGuidanceSheet topologySheet = Analyze([topologyEn, topologyJa]).Eligibility.Sheets.Single();
            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, topologySheet.Status);
            Assert.Contains(SourceGuidanceIncompatibilityReason.RowTopologyMismatch, topologySheet.IncompatibilityReasons);
            Assert.DoesNotContain(topologySheet.Columns, column => column.Role == SourceGuidanceRole.Translatable);
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
    public void MissingSheetIsEmittedAsIncompatibleAndUnknown()
    {
        string en = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]), Sheet("OnlyEnglish", ["Technical"]));
        string ja = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        try
        {
            SourceGuidanceBundle bundle = Analyze([en, ja]);
            SourceGuidanceSheet missing = bundle.Eligibility.Sheets.Single(sheet => sheet.Name == "OnlyEnglish");

            Assert.Equal(SourceGuidanceSheetStatus.Incompatible, missing.Status);
            Assert.Contains(SourceGuidanceIncompatibilityReason.MissingInInput, missing.IncompatibilityReasons);
            Assert.All(missing.Columns, column => Assert.Equal(SourceGuidanceRole.Unknown, column.Role));
        }
        finally
        {
            Delete(en);
            Delete(ja);
        }
    }

    [Fact]
    public void GuidanceIsDeterministicAndRoundTripsItsPersistedContract()
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
            new SourceGuidanceGenerator().Generate([fr, en, de, ja], secondOutput);

            Assert.Equal(File.ReadAllBytes(firstOutput), File.ReadAllBytes(secondOutput));
            SourceGuidanceBundle bundle = SourceGuidanceReader.Read(firstOutput);
            Assert.Equal(bundle.BundleId, SourceGuidanceReader.Read(secondOutput).BundleId);
            Assert.Null(bundle.Semantics);
            Assert.DoesNotContain(Path.GetFullPath(en), File.ReadAllText(firstOutput));
            Assert.EndsWith("\n", File.ReadAllText(firstOutput));
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
                .Select((value, columnIndex) => new HxsStringCellRecord(
                    rowId,
                    sourceRow.SubrowId,
                    columnIndex,
                    value,
                    sourceRow.RawValues is null ? null : sourceRow.RawValues[columnIndex],
                    HxsHashing.HashMacro(value),
                    sourceRow.RawValues is null || sourceRow.RawValues[columnIndex] is null
                        ? null
                        : HxsHashing.HashRaw(sourceRow.RawValues[columnIndex]!)))
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
}
