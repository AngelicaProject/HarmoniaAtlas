using HarmoniaAtlas.Game;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Extraction;

public sealed record ExtractionSummary(
    string GameVersion,
    string Language,
    string SnapshotId,
    string ContentId,
    int SheetCount,
    long RowCount,
    long StringCount,
    string OutputPath);

public sealed class ExtractionEngine
{
    public ExtractionSummary Extract(string gamePath, string language, string outputPath)
    {
        GameInstallation installation = GameInstallation.FromPath(gamePath);
        GameLanguage requestedLanguage = GameLanguageParser.Parse(language);
        string gameVersion = new GameVersionReader().Read(installation);
        string languageCode = requestedLanguage.ToCode();

        using LuminaSource source = LuminaSource.Open(installation, requestedLanguage);
        using HxsWriteSession writer = new HxsWriter().Begin(outputPath);

        List<HxsSheetRecord> sheets = new();
        long totalRows = 0;
        long totalStrings = 0;
        string[] sheetNames = source.SheetNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        foreach (string sheetName in sheetNames)
        {
            LuminaSheet sheet = source.OpenSheet(sheetName);
            HarmoniaSheetInfo info = sheet.Info;
            byte[] schemaHash = HxsHashing.HashSchema(info.Name, info.Variant, info.Columns);
            HxsSheetRecord startingSheet = new(
                info.Name,
                info.Variant,
                info.EffectiveLanguage,
                info.Columns,
                0,
                schemaHash,
                new byte[32],
                new byte[32],
                new byte[32]);
            int sheetId = writer.BeginSheet(startingSheet);
            HxsSheetHashAccumulator hashAccumulator = new(info.Name);
            int rowCount = 0;
            long stringCount = 0;

            foreach (HarmoniaRowData row in sheet.EnumerateRows())
            {
                HxsRowRecord rowRecord = RowCanonicalizer.Create(info.Name, row);
                writer.WriteRow(sheetId, rowRecord);
                hashAccumulator.AddRow(rowRecord);
                rowCount = checked(rowCount + 1);
                stringCount = checked(stringCount + rowRecord.StringCells.Count);
            }

            byte[] technicalHash = hashAccumulator.ComputeTechnicalHash();
            byte[] stringHash = hashAccumulator.ComputeStringHash();
            byte[] contentHash = HxsHashing.HashSheetContent(info.Name, info.Variant, schemaHash, technicalHash, stringHash);
            HxsSheetRecord completedSheet = startingSheet with
            {
                RowCount = rowCount,
                TechnicalHash = technicalHash,
                StringHash = stringHash,
                ContentHash = contentHash,
            };
            writer.CompleteSheet(sheetId, completedSheet);
            sheets.Add(completedSheet);
            totalRows = checked(totalRows + rowCount);
            totalStrings = checked(totalStrings + stringCount);
        }

        string contentId = HxsHashing.ComputeContentId(languageCode, sheets);
        string snapshotId = HxsHashing.ComputeSnapshotId(gameVersion, languageCode, contentId);
        writer.WriteMetadata(new HxsMetadata(
            HxsFormatVersion.Current,
            gameVersion,
            languageCode,
            "full",
            contentId,
            snapshotId,
            AtlasApplicationVersion.Current,
            LuminaVersion.Current,
            sheets.Count,
            totalRows,
            totalStrings));
        writer.Complete();

        return new ExtractionSummary(
            gameVersion,
            languageCode,
            snapshotId,
            contentId,
            sheets.Count,
            totalRows,
            totalStrings,
            Path.GetFullPath(outputPath));
    }
}

internal static class RowCanonicalizer
{
    public static HxsRowRecord Create(string sheetName, HarmoniaRowData row)
    {
        List<HxsTechnicalCell> technicalCells = new();
        List<HxsStringCellRecord> stringCells = new();
        foreach (HarmoniaCellData cell in row.Cells.OrderBy(cell => cell.ColumnIndex))
        {
            if (cell.Type == HarmoniaColumnType.String)
            {
                if (cell.MacroText is null)
                {
                    throw new InvalidDataException($"String cell {sheetName}/{row.RowId}/{row.SubrowId}/{cell.ColumnIndex} has no macro text.");
                }

                byte[]? rawHash = cell.RawValue is null ? null : HxsHashing.HashRaw(cell.RawValue);
                stringCells.Add(new HxsStringCellRecord(
                    row.RowId,
                    row.SubrowId,
                    cell.ColumnIndex,
                    cell.MacroText,
                    cell.RawValue,
                    HxsHashing.HashMacro(cell.MacroText),
                    rawHash));
            }
            else
            {
                if (cell.TechnicalValue is null)
                {
                    throw new InvalidDataException($"Technical cell {sheetName}/{row.RowId}/{row.SubrowId}/{cell.ColumnIndex} has no value.");
                }

                technicalCells.Add(new HxsTechnicalCell(
                    cell.ColumnIndex,
                    cell.Type,
                    HxsHashing.EncodeTechnicalValue(cell.Type, cell.TechnicalValue)));
            }
        }

        byte[] technicalPayload = HxsHashing.EncodeTechnicalPayload(technicalCells);
        byte[] technicalHash = HxsHashing.HashRowTechnical(sheetName, row.RowId, row.SubrowId, technicalCells);
        byte[] stringHash = HxsHashing.HashRowStrings(sheetName, row.RowId, row.SubrowId, stringCells);
        byte[] rowHash = HxsHashing.HashRow(sheetName, row.RowId, row.SubrowId, technicalHash, stringHash);
        return new HxsRowRecord(row.RowId, row.SubrowId, technicalPayload, rowHash, technicalHash, stringHash, stringCells);
    }
}
