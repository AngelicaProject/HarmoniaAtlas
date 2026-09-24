using System.Diagnostics.CodeAnalysis;
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
    string OutputPath,
    IReadOnlyList<HxsExcludedSheet> ExcludedSheets);

public sealed record ExtractionProgress(
    string Sheet,
    int SheetIndex,
    int SheetCount,
    long RowsProcessed,
    bool SheetCompleted);

public sealed class ExtractionEngine
{
    public ExtractionSummary Extract(
        string gamePath,
        string language,
        string outputPath,
        Action<ExtractionProgress>? progress = null)
    {
        GameInstallation installation = GameInstallation.FromPath(gamePath);
        GameLanguage requestedLanguage = GameLanguageParser.Parse(language);
        string gameVersion = new GameVersionReader().Read(installation);

        using LuminaSource source = LuminaSource.Open(installation, requestedLanguage);
        return Extract(source, gameVersion, requestedLanguage.ToCode(), outputPath, progress);
    }

    /// <summary>
    /// Extracts every sheet of <paramref name="source"/>. A sheet that raises
    /// <see cref="SheetReadException"/> is rolled back and recorded as excluded;
    /// any other failure aborts the snapshot. A catalog in which no sheet can be
    /// read is treated as a failure of the whole source.
    /// </summary>
    public ExtractionSummary Extract(
        IExtractionSource source,
        string gameVersion,
        string languageCode,
        string outputPath,
        Action<ExtractionProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using HxsWriteSession writer = new HxsWriter().Begin(outputPath);

        List<HxsSheetRecord> sheets = new();
        List<HxsExcludedSheet> excludedSheets = new();
        long totalRows = 0;
        long totalStrings = 0;
        string[] sheetNames = source.SheetNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        for (int index = 0; index < sheetNames.Length; index++)
        {
            string sheetName = sheetNames[index];
            int sheetIndex = index + 1;
            long rowsBefore = totalRows;
            progress?.Invoke(new ExtractionProgress(sheetName, sheetIndex, sheetNames.Length, totalRows, false));
            writer.BeginSheetScope();
            try
            {
                (HxsSheetRecord sheet, long stringCount) = ExtractSheet(
                    source,
                    sheetName,
                    writer,
                    rowsProcessed => progress?.Invoke(new ExtractionProgress(
                        sheetName,
                        sheetIndex,
                        sheetNames.Length,
                        rowsBefore + rowsProcessed,
                        false)));
                writer.CommitSheet();
                sheets.Add(sheet);
                totalRows = checked(totalRows + sheet.RowCount);
                totalStrings = checked(totalStrings + stringCount);
            }
            catch (SheetReadException exception)
            {
                writer.AbandonSheet();
                HxsExcludedSheet excluded = new(sheetName, exception.Reason);
                writer.WriteExcludedSheet(excluded);
                excludedSheets.Add(excluded);
            }

            progress?.Invoke(new ExtractionProgress(sheetName, sheetIndex, sheetNames.Length, totalRows, true));
        }

        if (sheetNames.Length > 0 && sheets.Count == 0)
        {
            throw new InvalidDataException("No sheet of the game catalog could be read; the installation or its data is unusable.");
        }

        string contentId = HxsHashing.ComputeContentId(languageCode, sheets, excludedSheets);
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
            totalStrings,
            excludedSheets.Count));
        writer.Complete();

        return new ExtractionSummary(
            gameVersion,
            languageCode,
            snapshotId,
            contentId,
            sheets.Count,
            totalRows,
            totalStrings,
            Path.GetFullPath(outputPath),
            excludedSheets);
    }

    private static (HxsSheetRecord Sheet, long StringCount) ExtractSheet(
        IExtractionSource source,
        string sheetName,
        HxsWriteSession writer,
        Action<long> progress)
    {
        IExtractionSheet sheet = source.OpenSheet(sheetName);
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
        long lastProgressTimestamp = Environment.TickCount64;

        using (SheetRowReader rows = new(sheet))
        {
            while (rows.TryReadNext(out HxsRowRecord? rowRecord))
            {
                writer.WriteRow(sheetId, rowRecord);
                hashAccumulator.AddRow(rowRecord);
                rowCount = checked(rowCount + 1);
                stringCount = checked(stringCount + rowRecord.StringCells.Count);
                if (rowCount % 1000 == 0 || Environment.TickCount64 - lastProgressTimestamp >= 250)
                {
                    progress(rowCount);
                    lastProgressTimestamp = Environment.TickCount64;
                }
            }
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
        return (completedSheet, stringCount);
    }
}

/// <summary>
/// Reads and canonicalizes the rows of one sheet. Every failure raised by the
/// game data or by canonicalization is attributed to the sheet; failures of the
/// HXS writer never pass through this reader.
/// </summary>
internal sealed class SheetRowReader : IDisposable
{
    private readonly string _sheetName;
    private readonly IEnumerator<HarmoniaRowData> _rows;

    public SheetRowReader(IExtractionSheet sheet)
    {
        _sheetName = sheet.Info.Name;
        try
        {
            _rows = sheet.EnumerateRows().GetEnumerator();
        }
        catch (Exception exception) when (SheetReadException.IsSheetScoped(exception))
        {
            throw Unreadable(exception);
        }
    }

    public bool TryReadNext([NotNullWhen(true)] out HxsRowRecord? row)
    {
        try
        {
            if (!_rows.MoveNext())
            {
                row = null;
                return false;
            }

            row = RowCanonicalizer.Create(_sheetName, _rows.Current);
            return true;
        }
        catch (SheetReadException)
        {
            throw;
        }
        catch (Exception exception) when (SheetReadException.IsSheetScoped(exception))
        {
            throw Unreadable(exception);
        }
    }

    public void Dispose()
    {
        try
        {
            _rows.Dispose();
        }
        catch (Exception exception) when (SheetReadException.IsSheetScoped(exception))
        {
            throw Unreadable(exception);
        }
    }

    private SheetReadException Unreadable(Exception exception) =>
        new(_sheetName, HxsSheetExclusionReason.UnreadableData, $"Sheet '{_sheetName}' cannot be read: {exception.Message}", exception);
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
