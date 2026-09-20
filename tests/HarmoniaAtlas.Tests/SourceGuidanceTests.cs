using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze(path, []));
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
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze(duplicateEn, [duplicateEn2]));
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze(gameEn, [gameJa]));
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze(scopeEn, [scopeJa]));
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
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceAnalyzer().Analyze(invalid, [valid]));
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
        string jaOtherRaw = CreateSnapshot("ja", new SheetSpec(
            "Item",
            [new HarmoniaColumnDefinition(0, 0, HarmoniaColumnType.String)],
            [new RowSpec(["same"], RawValues: new byte[]?[] { [3] })]));
        try
        {
            SourceGuidanceBundle first = Analyze(en, [ja]);
            SourceGuidanceBundle second = Analyze(en, [jaOtherRaw]);

            Assert.Empty(first.Sheets.Single().Translatable);
            Assert.Equal(
                first.EvidenceInputs.Single(input => input.Language == "ja").EvidenceId,
                second.EvidenceInputs.Single(input => input.Language == "ja").EvidenceId);
            Assert.Equal(first.BundleId, second.BundleId);
            Assert.NotEqual(HxsVerifier.Verify(ja).Metadata.ContentId, HxsVerifier.Verify(jaOtherRaw).Metadata.ContentId);
        }
        finally
        {
            Delete(en);
            Delete(ja);
            Delete(jaOtherRaw);
        }
    }

    [Fact]
    public void TechnicalValueDifferencesAloneDoNotChangeEvidenceId()
    {
        string source = CreateTechnicalSnapshot("en", 10);
        string sourceWithTechnicalChange = CreateTechnicalSnapshot("en", 11);
        string japanese = CreateTechnicalSnapshot("ja", 20);
        try
        {
            SourceGuidanceBundle first = Analyze(source, [japanese]);
            SourceGuidanceBundle second = Analyze(sourceWithTechnicalChange, [japanese]);

            Assert.NotEqual(HxsVerifier.Verify(source).Metadata.ContentId, HxsVerifier.Verify(sourceWithTechnicalChange).Metadata.ContentId);
            Assert.Equal(
                first.EvidenceInputs.Single(input => input.Language == "en").EvidenceId,
                second.EvidenceInputs.Single(input => input.Language == "en").EvidenceId);
            Assert.Equal(
                first.EvidenceInputs.Single(input => input.Language == "ja").EvidenceId,
                second.EvidenceInputs.Single(input => input.Language == "ja").EvidenceId);
        }
        finally
        {
            Delete(source);
            Delete(sourceWithTechnicalChange);
            Delete(japanese);
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
            new SourceGuidanceGenerator().Generate(en, [ja, de, fr], firstOutput);
            new SourceGuidanceGenerator().Generate(en, [fr, de, ja], secondOutput);

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

            string originalJson = File.ReadAllText(firstOutput);
            string tamperedSource = originalJson.Replace(
                bundle.Source.ContentId,
                "sha256:" + new string('0', 64),
                StringComparison.Ordinal);
            File.WriteAllText(firstOutput, tamperedSource);
            Assert.Throws<SourceGuidanceFormatException>(() => SourceGuidanceReader.Read(firstOutput));

            string tamperedEvidence = originalJson.Replace(
                bundle.EvidenceInputs[0].EvidenceId,
                "sha256:" + new string('1', 64),
                StringComparison.Ordinal);
            File.WriteAllText(firstOutput, tamperedEvidence);
            Assert.Throws<SourceGuidanceFormatException>(() => SourceGuidanceReader.Read(firstOutput));

            string tampered = originalJson.Replace("\"columnIndex\":0", "\"columnIndex\":1", StringComparison.Ordinal);
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
    public void GuidancePersistsExactSourceIdentityAndLightweightEvidenceInputs()
    {
        string source = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string japanese = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        string german = CreateSnapshot("de", Sheet("Item", ["Feuerscherbe"]));
        string french = CreateSnapshot("fr", Sheet("Item", ["Éclat de feu"]));
        string output = NewPath(".hsg.json");
        try
        {
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], output);

            SourceGuidanceBundle bundle = SourceGuidanceReader.Read(output);
            HxsMetadata sourceMetadata = HxsVerifier.Verify(source).Metadata;
            Assert.Equal("en", bundle.Source.Language);
            Assert.Equal(sourceMetadata.ContentId, bundle.Source.ContentId);
            Assert.Equal(sourceMetadata.SnapshotId, bundle.Source.SnapshotId);
            Assert.Equal(["de", "en", "fr", "ja"], bundle.EvidenceInputs.Select(input => input.Language));
            Assert.Equal(1, bundle.EvidenceInputs.Count(input => input.Language == bundle.Source.Language));

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
            foreach (JsonElement evidenceInput in document.RootElement.GetProperty("evidenceInputs").EnumerateArray())
            {
                Assert.False(evidenceInput.TryGetProperty("contentId", out _));
                Assert.False(evidenceInput.TryGetProperty("snapshotId", out _));
                Assert.True(evidenceInput.TryGetProperty("evidenceId", out _));
            }
        }
        finally
        {
            Delete(source);
            Delete(japanese);
            Delete(german);
            Delete(french);
            Delete(output);
        }
    }

    [Fact]
    public void ReaderRejectsNonCanonicalSourceAndEvidenceLanguagesEvenWithMatchingBundleIds()
    {
        string source = CreateSnapshot("en", Sheet("Item", ["Fire Shard"]));
        string japanese = CreateSnapshot("ja", Sheet("Item", ["ファイアシャード"]));
        string output = NewPath(".hsg.json");
        try
        {
            new SourceGuidanceGenerator().Generate(source, [japanese], output);
            SourceGuidanceBundle bundle = SourceGuidanceReader.Read(output);

            SourceGuidanceBundle invalidSource = WithComputedBundleId(bundle with
            {
                Source = bundle.Source with { Language = "pirate" },
            });
            WriteBundleForTest(invalidSource, output);
            Assert.Throws<SourceGuidanceFormatException>(() => SourceGuidanceReader.Read(output));

            SourceGuidanceBundle invalidEvidence = WithComputedBundleId(bundle with
            {
                EvidenceInputs = bundle.EvidenceInputs
                    .Select(input => input.Language == "en" ? input with { Language = "english" } : input)
                    .ToArray(),
            });
            WriteBundleForTest(invalidEvidence, output);
            Assert.Throws<SourceGuidanceFormatException>(() => SourceGuidanceReader.Read(output));
        }
        finally
        {
            Delete(source);
            Delete(japanese);
            Delete(output);
        }
    }

    [Fact]
    public void EvidenceHasherIsDeterministicAndSensitiveOnlyToEvidenceFields()
    {
        byte[] schemaHash = new byte[32];
        string first = HashEvidence("en", schemaHash, (1u, (ushort)0, 0, "Hello"));
        string repeated = HashEvidence("en", schemaHash, (1u, (ushort)0, 0, "Hello"));
        string macroChanged = HashEvidence("en", schemaHash, (1u, (ushort)0, 0, "こんにちは"));
        string topologyChanged = HashEvidence("en", schemaHash, (2u, (ushort)0, 0, "Hello"));
        byte[] changedSchema = new byte[32];
        changedSchema[0] = 1;
        string schemaChanged = HashEvidence("en", changedSchema, (1u, (ushort)0, 0, "Hello"));

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, macroChanged);
        Assert.NotEqual(first, topologyChanged);
        Assert.NotEqual(first, schemaChanged);
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
            Assert.Throws<SourceGuidanceException>(() => new SourceGuidanceGenerator().Generate(invalid, [valid], output));
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

    private static SourceGuidanceBundle Analyze(IReadOnlyList<string> paths) =>
        new SourceGuidanceAnalyzer().Analyze(paths[0], paths.Skip(1).ToArray());

    private static SourceGuidanceBundle Analyze(string sourcePath, IReadOnlyList<string> comparePaths) =>
        new SourceGuidanceAnalyzer().Analyze(sourcePath, comparePaths);

    private static string HashEvidence(
        string language,
        byte[] schemaHash,
        params (uint RowId, ushort SubrowId, int ColumnIndex, string MacroText)[] occurrences)
    {
        using SourceGuidanceEvidenceHasher hasher = new("game", "full", language);
        hasher.AddSheet("Item", HarmoniaSheetVariant.DefaultRows, schemaHash);
        foreach (IGrouping<(uint RowId, ushort SubrowId), (uint RowId, ushort SubrowId, int ColumnIndex, string MacroText)> row in occurrences
            .GroupBy(occurrence => (occurrence.RowId, occurrence.SubrowId))
            .OrderBy(group => group.Key.RowId)
            .ThenBy(group => group.Key.SubrowId))
        {
            hasher.AddRow(row.Key.RowId, row.Key.SubrowId);
            foreach ((uint _, ushort _, int columnIndex, string macroText) in row.OrderBy(occurrence => occurrence.ColumnIndex))
            {
                hasher.AddStringOccurrence(columnIndex, macroText);
            }
        }

        return hasher.ComputeEvidenceId();
    }

    private static SourceGuidanceBundle WithComputedBundleId(SourceGuidanceBundle bundle) =>
        bundle with { BundleId = SourceGuidanceHashing.ComputeBundleId(bundle) };

    private static void WriteBundleForTest(SourceGuidanceBundle bundle, string path)
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(bundle, options) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

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

    private static string CreateTechnicalSnapshot(string language, int technicalValue)
    {
        string path = NewPath();
        const string sheetName = "Technical";
        HarmoniaColumnDefinition[] columns =
        [
            new(0, 0, HarmoniaColumnType.String),
            new(1, 4, HarmoniaColumnType.Int32),
        ];
        HxsTechnicalCell technicalCell = new(
            1,
            HarmoniaColumnType.Int32,
            HxsHashing.EncodeTechnicalValue(HarmoniaColumnType.Int32, technicalValue));
        HxsStringCellRecord stringCell = new(
            1,
            0,
            0,
            "same",
            null,
            HxsHashing.HashMacro("same"),
            null);
        byte[] technicalHash = HxsHashing.HashRowTechnical(sheetName, 1, 0, [technicalCell]);
        byte[] stringHash = HxsHashing.HashRowStrings(sheetName, 1, 0, [stringCell]);
        HxsRowRecord row = new(
            1,
            0,
            HxsHashing.EncodeTechnicalPayload([technicalCell]),
            HxsHashing.HashRow(sheetName, 1, 0, technicalHash, stringHash),
            technicalHash,
            stringHash,
            [stringCell]);
        byte[] schemaHash = HxsHashing.HashSchema(sheetName, HarmoniaSheetVariant.DefaultRows, columns);
        HxsSheetRecord sheet = new(
            sheetName,
            HarmoniaSheetVariant.DefaultRows,
            "none",
            columns,
            1,
            schemaHash,
            HxsHashing.HashSheetTechnical(sheetName, [row]),
            HxsHashing.HashSheetStrings(sheetName, [row]),
            HxsHashing.HashSheetContent(
                sheetName,
                HarmoniaSheetVariant.DefaultRows,
                schemaHash,
                HxsHashing.HashSheetTechnical(sheetName, [row]),
                HxsHashing.HashSheetStrings(sheetName, [row])));
        string contentId = HxsHashing.ComputeContentId(language, [sheet]);
        using (HxsWriteSession session = new HxsWriter().Begin(path))
        {
            int sheetId = session.BeginSheet(sheet);
            session.WriteRow(sheetId, row);
            session.CompleteSheet(sheetId, sheet);
            session.WriteMetadata(new HxsMetadata(
                1,
                "game",
                language,
                "full",
                contentId,
                HxsHashing.ComputeSnapshotId("game", language, contentId),
                "test",
                "7.7.0",
                1,
                1,
                1));
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
