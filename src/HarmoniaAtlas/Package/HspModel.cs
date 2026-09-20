using System.Text.Json.Serialization;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Package;

public sealed record HspSourceIdentity(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("contentId")] string ContentId,
    [property: JsonPropertyName("snapshotId")] string SnapshotId);

public sealed record HspComponentDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record HspManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("packageId")] string PackageId,
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("source")] HspSourceIdentity Source,
    [property: JsonPropertyName("components")] IReadOnlyList<HspComponentDescriptor> Components);

public sealed record HspPackageSummary(
    HspManifest Manifest,
    HxsMetadata SourceMetadata,
    SourceGuidanceBundle Guidance,
    string OutputPath);

public sealed class HspFormatException : FormatException
{
    public HspFormatException(string message)
        : base(message)
    {
    }

    public HspFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
