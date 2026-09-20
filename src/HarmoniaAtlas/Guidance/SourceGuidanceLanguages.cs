namespace HarmoniaAtlas.Guidance;

internal static class SourceGuidanceLanguages
{
    private static readonly HashSet<string> CanonicalSourceLanguages = new(StringComparer.Ordinal)
    {
        "en",
        "ja",
        "de",
        "fr",
        "zh-cn",
        "zh-tw",
        "ko",
    };

    public static bool IsCanonicalSourceLanguage(string language) => CanonicalSourceLanguages.Contains(language);
}
