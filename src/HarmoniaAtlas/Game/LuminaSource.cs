using Lumina;
using Lumina.Data;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Game;

public sealed class LuminaSource : IDisposable
{
    private readonly GameData _gameData;
    private readonly Language _language;

    private LuminaSource(GameInstallation installation, GameLanguage language, GameData gameData)
    {
        Installation = installation;
        Language = language;
        _language = language.ToLuminaLanguage();
        _gameData = gameData;
    }

    public GameInstallation Installation { get; }

    public GameLanguage Language { get; }

    public IReadOnlyList<string> SheetNames => _gameData.Excel.SheetNames;

    public static LuminaSource Open(GameInstallation installation, GameLanguage language)
    {
        ArgumentNullException.ThrowIfNull(installation);

        Language luminaLanguage = language.ToLuminaLanguage();
        LuminaOptions options = new()
        {
            DefaultExcelLanguage = luminaLanguage,
        };

        GameData gameData = new(installation.FullPath, options);
        return new LuminaSource(installation, language, gameData);
    }

    public static LuminaSource Open(string gamePath, string language)
    {
        GameInstallation installation = GameInstallation.FromPath(gamePath);
        return Open(installation, GameLanguageParser.Parse(language));
    }

    public IReadOnlyList<HarmoniaColumnType> GetColumnTypes(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        var rawSheet = _gameData.Excel.GetRawSheet(sheetName, _language);
        return rawSheet.Columns.Select(column => LuminaColumnTypeMapper.Map(column.Type)).ToArray();
    }

    public LuminaSheet OpenSheet(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);

        RawExcelSheet rawSheet = _gameData.Excel.GetRawSheet(sheetName, _language);
        HarmoniaSheetVariant harmoniaVariant = rawSheet is RawSubrowExcelSheet
            ? HarmoniaSheetVariant.Subrows
            : rawSheet.GetType() == typeof(RawExcelSheet)
                ? HarmoniaSheetVariant.DefaultRows
                : throw new NotSupportedException($"Unsupported Lumina sheet variant for '{sheetName}'.");

        IReadOnlyList<HarmoniaColumnDefinition> columns = rawSheet.Columns
            .Select((column, index) => new HarmoniaColumnDefinition(index, column.Offset, LuminaColumnTypeMapper.Map(column.Type)))
            .ToArray();
        string effectiveLanguage = rawSheet.Language == Lumina.Data.Language.None
            ? GameLanguageParser.ToCode(_language)
            : GameLanguageParser.ToCode(rawSheet.Language);
        HarmoniaSheetInfo info = new(sheetName, harmoniaVariant, effectiveLanguage, columns);
        return harmoniaVariant == HarmoniaSheetVariant.DefaultRows
            ? new LuminaSheet(rawSheet, info, _gameData.Excel.GetSheet<RawRow>(_language, sheetName), null)
            : new LuminaSheet(rawSheet, info, null, _gameData.Excel.GetSubrowSheet<RawSubrow>(_language, sheetName));
    }

    public void Dispose() => _gameData.Dispose();
}
