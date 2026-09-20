using System.Text.Json.Serialization;

namespace HarmoniaAtlas.Guidance;

public enum SourceGuidanceSheetStatus
{
    Compatible,
    Incompatible,
}

public enum SourceGuidanceIncompatibilityReason
{
    MissingInInput,
    SheetVariantMismatch,
    ColumnDefinitionMismatch,
    SchemaHashMismatch,
    RowTopologyMismatch,
}

public sealed record SourceGuidanceSourceIdentity(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("contentId")] string ContentId,
    [property: JsonPropertyName("snapshotId")] string SnapshotId);

public sealed record SourceGuidanceEvidenceInput(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("evidenceId")] string EvidenceId);

public sealed record SourceGuidanceOccurrence(
    [property: JsonPropertyName("rowId")] uint RowId,
    [property: JsonPropertyName("subrowId")] ushort SubrowId,
    [property: JsonPropertyName("columnIndex")] int ColumnIndex);

public sealed record SourceGuidanceSheet(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("schemaHash")] string SchemaHash,
    [property: JsonPropertyName("status")] SourceGuidanceSheetStatus Status,
    [property: JsonPropertyName("translatable")] IReadOnlyList<SourceGuidanceOccurrence> Translatable,
    [property: JsonPropertyName("incompatibilityReasons")] IReadOnlyList<SourceGuidanceIncompatibilityReason> IncompatibilityReasons);

public sealed record SourceGuidanceBundle(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("source")] SourceGuidanceSourceIdentity Source,
    [property: JsonPropertyName("evidenceInputs")] IReadOnlyList<SourceGuidanceEvidenceInput> EvidenceInputs,
    [property: JsonPropertyName("sheets")] IReadOnlyList<SourceGuidanceSheet> Sheets);

public sealed record SourceGuidanceSummary(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    [property: JsonPropertyName("compatibleSheetCount")] int CompatibleSheetCount,
    [property: JsonPropertyName("incompatibleSheetCount")] int IncompatibleSheetCount,
    [property: JsonPropertyName("translatableOccurrenceCount")] int TranslatableOccurrenceCount,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("outputPath")] string OutputPath);

public class SourceGuidanceException : Exception
{
    public SourceGuidanceException(string message)
        : base(message)
    {
    }

    public SourceGuidanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SourceGuidanceFormatException : SourceGuidanceException
{
    public SourceGuidanceFormatException(string message)
        : base(message)
    {
    }

    public SourceGuidanceFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
