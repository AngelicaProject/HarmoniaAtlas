using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmoniaAtlas.Guidance;

public enum SourceGuidanceRole
{
    Translatable,
    Context,
    Technical,
    Unknown,
}

public enum SourceGuidanceEvidenceKind
{
    OfficialLanguageVariance,
    KnownTechnicalNamespace,
    NoOfficialLanguageVariance,
    IncompatibleSourceLayout,
}

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

public sealed record SourceGuidanceInput(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("contentId")] string ContentId,
    [property: JsonPropertyName("snapshotId")] string SnapshotId);

public sealed record SourceGuidanceEvidence(
    [property: JsonPropertyName("kind")] SourceGuidanceEvidenceKind Kind,
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    [property: JsonPropertyName("comparableOccurrences")] long ComparableOccurrences,
    [property: JsonPropertyName("varyingOccurrences")] long VaryingOccurrences,
    [property: JsonPropertyName("prefix"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Prefix = null);

public sealed record SourceGuidanceColumn(
    [property: JsonPropertyName("columnIndex")] int ColumnIndex,
    [property: JsonPropertyName("role")] SourceGuidanceRole Role,
    [property: JsonPropertyName("evidence")] SourceGuidanceEvidence Evidence);

public sealed record SourceGuidanceSheet(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] SourceGuidanceSheetStatus Status,
    [property: JsonPropertyName("schemaHash")] string SchemaHash,
    [property: JsonPropertyName("columns")] IReadOnlyList<SourceGuidanceColumn> Columns,
    [property: JsonPropertyName("incompatibilityReasons")] IReadOnlyList<SourceGuidanceIncompatibilityReason> IncompatibilityReasons);

public sealed record SourceGuidanceEligibility(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sheets")] IReadOnlyList<SourceGuidanceSheet> Sheets);

public sealed record SourceGuidanceBundle(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("bundleId")] string BundleId,
    [property: JsonPropertyName("inputs")] IReadOnlyList<SourceGuidanceInput> Inputs,
    [property: JsonPropertyName("eligibility")] SourceGuidanceEligibility Eligibility,
    [property: JsonPropertyName("semantics")] JsonElement? Semantics);

public sealed record SourceGuidanceSummary(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    [property: JsonPropertyName("compatibleSheetCount")] int CompatibleSheetCount,
    [property: JsonPropertyName("incompatibleSheetCount")] int IncompatibleSheetCount,
    [property: JsonPropertyName("translatableColumnCount")] int TranslatableColumnCount,
    [property: JsonPropertyName("contextColumnCount")] int ContextColumnCount,
    [property: JsonPropertyName("unknownColumnCount")] int UnknownColumnCount,
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
