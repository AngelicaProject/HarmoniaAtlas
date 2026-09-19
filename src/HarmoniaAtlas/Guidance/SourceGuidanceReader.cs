using System.Text;
using System.Text.Json;

namespace HarmoniaAtlas.Guidance;

public static class SourceGuidanceReader
{
    public static SourceGuidanceBundle Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            string json = File.ReadAllText(Path.GetFullPath(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
            SourceGuidanceBundle bundle = JsonSerializer.Deserialize<SourceGuidanceBundle>(json, SourceGuidanceJson.Options)
                ?? throw new SourceGuidanceFormatException("Source guidance JSON is empty.");
            Validate(bundle);
            return bundle;
        }
        catch (SourceGuidanceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SourceGuidanceFormatException("Source guidance JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new SourceGuidanceFormatException("Source guidance JSON contains an invalid value.", exception);
        }
    }

    public static void Validate(SourceGuidanceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.FormatVersion != 1 || string.IsNullOrWhiteSpace(bundle.GameVersion) || string.IsNullOrWhiteSpace(bundle.Scope) ||
            bundle.Inputs is null || bundle.Eligibility is null || bundle.Eligibility.Sheets is null ||
            bundle.Semantics.HasValue || !SourceGuidanceHashing.IsSha256(bundle.BundleId))
        {
            throw new SourceGuidanceFormatException("Source guidance metadata is invalid.");
        }

        ValidateInputs(bundle.Inputs);
        if (bundle.Eligibility.Version != 1)
        {
            throw new SourceGuidanceFormatException("Unsupported source guidance eligibility version.");
        }

        string? previousSheet = null;
        foreach (SourceGuidanceSheet sheet in bundle.Eligibility.Sheets)
        {
            if (sheet is null || string.IsNullOrWhiteSpace(sheet.Name) || !SourceGuidanceHashing.IsSha256(sheet.SchemaHash) ||
                (previousSheet is not null && string.CompareOrdinal(previousSheet, sheet.Name) >= 0))
            {
                throw new SourceGuidanceFormatException("Source guidance sheets are invalid or not in ordinal order.");
            }

            previousSheet = sheet.Name;
            ValidateReasons(sheet);
            ValidateColumns(sheet, bundle.Inputs.Select(input => input.Language).ToArray());
        }

        string expectedBundleId;
        try
        {
            expectedBundleId = SourceGuidanceHashing.ComputeBundleId(bundle);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new SourceGuidanceFormatException("Source guidance cannot be canonically hashed.", exception);
        }

        if (!string.Equals(bundle.BundleId, expectedBundleId, StringComparison.Ordinal))
        {
            throw new SourceGuidanceFormatException("Source guidance bundleId does not match its canonical content.");
        }
    }

    private static void ValidateInputs(IReadOnlyList<SourceGuidanceInput> inputs)
    {
        if (inputs.Count < 2)
        {
            throw new SourceGuidanceFormatException("Source guidance requires at least two inputs.");
        }

        string? previousLanguage = null;
        HashSet<string> languages = new(StringComparer.Ordinal);
        foreach (SourceGuidanceInput input in inputs)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.Language) || !SourceGuidanceHashing.IsSha256(input.ContentId) ||
                !SourceGuidanceHashing.IsSha256(input.SnapshotId) || !languages.Add(input.Language) ||
                (previousLanguage is not null && string.CompareOrdinal(previousLanguage, input.Language) >= 0))
            {
                throw new SourceGuidanceFormatException("Source guidance inputs are invalid or not in ordinal language order.");
            }

            previousLanguage = input.Language;
        }
    }

    private static void ValidateReasons(SourceGuidanceSheet sheet)
    {
        if (!Enum.IsDefined(sheet.Status) ||
            sheet.IncompatibilityReasons is null ||
            sheet.Status == SourceGuidanceSheetStatus.Compatible && sheet.IncompatibilityReasons.Count != 0 ||
            sheet.Status == SourceGuidanceSheetStatus.Incompatible && sheet.IncompatibilityReasons.Count == 0 ||
            sheet.IncompatibilityReasons.Distinct().Count() != sheet.IncompatibilityReasons.Count)
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has invalid compatibility reasons.");
        }

        foreach (SourceGuidanceIncompatibilityReason reason in sheet.IncompatibilityReasons)
        {
            if (!Enum.IsDefined(reason))
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has an unsupported compatibility reason.");
            }
        }
    }

    private static void ValidateColumns(SourceGuidanceSheet sheet, IReadOnlyList<string> inputLanguages)
    {
        if (sheet.Columns is null)
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has missing columns.");
        }

        int previousIndex = -1;
        foreach (SourceGuidanceColumn column in sheet.Columns)
        {
            if (column is null || column.ColumnIndex < 0 || column.ColumnIndex <= previousIndex || !Enum.IsDefined(column.Role))
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has invalid columns.");
            }

            previousIndex = column.ColumnIndex;
            if (sheet.Status == SourceGuidanceSheetStatus.Incompatible && column.Role == SourceGuidanceRole.Translatable)
            {
                throw new SourceGuidanceFormatException($"Incompatible source-guidance sheet '{sheet.Name}' cannot contain a translatable column.");
            }

            ValidateEvidence(sheet.Name, column, inputLanguages);
        }
    }

    private static void ValidateEvidence(string sheetName, SourceGuidanceColumn column, IReadOnlyList<string> inputLanguages)
    {
        if (column.Evidence is null)
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has missing evidence.");
        }

        SourceGuidanceEvidence evidence = column.Evidence;
        if (!Enum.IsDefined(evidence.Kind) || evidence.ComparableOccurrences < 0 || evidence.VaryingOccurrences < 0 ||
            evidence.VaryingOccurrences > evidence.ComparableOccurrences ||
            evidence.Languages is null || evidence.Languages.Count == 0 || evidence.Languages.Distinct(StringComparer.Ordinal).Count() != evidence.Languages.Count ||
            !evidence.Languages.SequenceEqual(evidence.Languages.OrderBy(language => language, StringComparer.Ordinal), StringComparer.Ordinal) ||
            !evidence.Languages.SequenceEqual(inputLanguages, StringComparer.Ordinal))
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has invalid evidence.");
        }

        if (evidence.Kind == SourceGuidanceEvidenceKind.KnownTechnicalNamespace)
        {
            if (column.Role != SourceGuidanceRole.Context || !string.Equals(evidence.Prefix, "TEXT_", StringComparison.Ordinal) || evidence.ComparableOccurrences != 0 || evidence.VaryingOccurrences != 0)
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has invalid namespace evidence.");
            }
        }
        else if (evidence.Kind == SourceGuidanceEvidenceKind.OfficialLanguageVariance)
        {
            if (column.Role != SourceGuidanceRole.Translatable || evidence.VaryingOccurrences == 0)
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has invalid variance evidence.");
            }
        }
        else if (evidence.Kind == SourceGuidanceEvidenceKind.NoOfficialLanguageVariance)
        {
            if (column.Role != SourceGuidanceRole.Unknown || evidence.VaryingOccurrences != 0)
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has invalid invariant evidence.");
            }
        }
        else if (evidence.Kind == SourceGuidanceEvidenceKind.IncompatibleSourceLayout &&
                 (column.Role != SourceGuidanceRole.Unknown || evidence.ComparableOccurrences != 0 || evidence.VaryingOccurrences != 0))
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has invalid incompatible-layout evidence.");
        }

        if (evidence.Kind != SourceGuidanceEvidenceKind.KnownTechnicalNamespace && evidence.Prefix is not null)
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheetName}' has an unexpected evidence prefix.");
        }
    }
}
