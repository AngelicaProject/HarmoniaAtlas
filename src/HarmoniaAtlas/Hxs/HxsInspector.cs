using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmoniaAtlas.Hxs;

public sealed record HxsInspection(
    [property: JsonPropertyName("hxsVersion")] int HxsVersion,
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("snapshotId")] string SnapshotId,
    [property: JsonPropertyName("contentId")] string ContentId,
    [property: JsonPropertyName("extractorVersion")] string ExtractorVersion,
    [property: JsonPropertyName("luminaVersion")] string LuminaVersion,
    [property: JsonPropertyName("sheetCount")] long SheetCount,
    [property: JsonPropertyName("rowCount")] long RowCount,
    [property: JsonPropertyName("stringCellCount")] long StringCellCount)
{
    public static HxsInspection FromMetadata(HxsMetadata metadata) => new(
        metadata.FormatVersion,
        metadata.GameVersion,
        metadata.Language,
        metadata.Scope,
        metadata.SnapshotId,
        metadata.ContentId,
        metadata.ExtractorVersion,
        metadata.LuminaVersion,
        metadata.SheetCount,
        metadata.RowCount,
        metadata.StringCellCount);

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public string ToText() => string.Join(
        Environment.NewLine,
        $"HXS version: {HxsVersion}",
        $"Game version: {GameVersion}",
        $"Language: {Language}",
        $"Scope: {Scope}",
        $"SnapshotId: {SnapshotId}",
        $"ContentId: {ContentId}",
        $"Extractor version: {ExtractorVersion}",
        $"Lumina version: {LuminaVersion}",
        $"Sheet count: {SheetCount}",
        $"Row count: {RowCount}",
        $"String cell count: {StringCellCount}");
}

public static class HxsInspector
{
    public static HxsInspection Inspect(string path)
    {
        HxsVerificationResult verification = HxsVerifier.Verify(path);
        return HxsInspection.FromMetadata(verification.Metadata);
    }
}
