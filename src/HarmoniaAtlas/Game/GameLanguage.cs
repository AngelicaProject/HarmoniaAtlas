using Lumina.Data;

namespace HarmoniaAtlas.Game;

public enum GameLanguage
{
    Japanese,
    English,
    German,
    French,
    ChineseSimplified,
    ChineseTraditional,
    Korean,
    TraditionalChinese,
}

public static class GameLanguageParser
{
    public static GameLanguage Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToLowerInvariant() switch
        {
            "japanese" or "ja" => GameLanguage.Japanese,
            "english" or "en" => GameLanguage.English,
            "german" or "de" => GameLanguage.German,
            "french" or "fr" => GameLanguage.French,
            "chinesesimplified" or "zh-cn" => GameLanguage.ChineseSimplified,
            "chinesetraditional" or "zh-tw" => GameLanguage.ChineseTraditional,
            "korean" or "ko" => GameLanguage.Korean,
            "traditionalchinese" => GameLanguage.TraditionalChinese,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported game language."),
        };
    }

    public static Language ToLuminaLanguage(this GameLanguage language) => language switch
    {
        GameLanguage.Japanese => Language.Japanese,
        GameLanguage.English => Language.English,
        GameLanguage.German => Language.German,
        GameLanguage.French => Language.French,
        GameLanguage.ChineseSimplified => Language.ChineseSimplified,
        GameLanguage.ChineseTraditional => Language.ChineseTraditional,
        GameLanguage.Korean => Language.Korean,
        GameLanguage.TraditionalChinese => Language.TraditionalChinese,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported game language."),
    };

    public static string ToCode(this GameLanguage language) => language switch
    {
        GameLanguage.Japanese => "ja",
        GameLanguage.English => "en",
        GameLanguage.German => "de",
        GameLanguage.French => "fr",
        GameLanguage.ChineseSimplified => "zh-cn",
        GameLanguage.ChineseTraditional or GameLanguage.TraditionalChinese => "zh-tw",
        GameLanguage.Korean => "ko",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported game language."),
    };

    public static string ToCode(Language language) => language switch
    {
        Language.Japanese => "ja",
        Language.English => "en",
        Language.German => "de",
        Language.French => "fr",
        Language.ChineseSimplified => "zh-cn",
        Language.ChineseTraditional or Language.TraditionalChinese => "zh-tw",
        Language.Korean => "ko",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported Lumina language."),
    };
}
