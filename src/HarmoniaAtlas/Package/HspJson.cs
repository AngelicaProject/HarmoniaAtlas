using System.Text.Json;

namespace HarmoniaAtlas.Package;

internal static class HspJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    public static byte[] SerializeManifest(HspManifest manifest)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", manifest.FormatVersion);
            writer.WriteString("packageId", manifest.PackageId);
            writer.WriteString("gameVersion", manifest.GameVersion);
            writer.WriteString("scope", manifest.Scope);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("language", manifest.Source.Language);
            writer.WriteString("contentId", manifest.Source.ContentId);
            writer.WriteString("snapshotId", manifest.Source.SnapshotId);
            writer.WriteEndObject();
            writer.WritePropertyName("components");
            writer.WriteStartArray();
            foreach (HspComponentDescriptor component in manifest.Components)
            {
                writer.WriteStartObject();
                writer.WriteString("id", component.Id);
                writer.WriteString("kind", component.Kind);
                writer.WriteNumber("formatVersion", component.FormatVersion);
                writer.WriteBoolean("required", component.Required);
                writer.WriteString("path", component.Path);
                writer.WriteNumber("size", component.Size);
                writer.WriteString("sha256", component.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] json = stream.ToArray();
        Array.Resize(ref json, json.Length + 1);
        json[^1] = (byte)'\n';
        return json;
    }

    public static HspManifest DeserializeManifest(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<HspManifest>(bytes, Options)
                ?? throw new HspFormatException("HSP manifest is empty.");
        }
        catch (HspFormatException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new HspFormatException("HSP manifest JSON is invalid.", exception);
        }
    }
}
