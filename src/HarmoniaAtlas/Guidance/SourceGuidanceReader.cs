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
        if (bundle.FormatVersion != 1 || string.IsNullOrWhiteSpace(bundle.GameVersion) ||
            string.IsNullOrWhiteSpace(bundle.Scope) || bundle.Inputs is null || bundle.Sheets is null ||
            !SourceGuidanceHashing.IsSha256(bundle.BundleId))
        {
            throw new SourceGuidanceFormatException("Source guidance metadata is invalid.");
        }

        ValidateInputs(bundle.Inputs);
        string? previousSheet = null;
        foreach (SourceGuidanceSheet sheet in bundle.Sheets)
        {
            if (sheet is null || string.IsNullOrWhiteSpace(sheet.Name) || !SourceGuidanceHashing.IsSha256(sheet.SchemaHash) ||
                (previousSheet is not null && string.CompareOrdinal(previousSheet, sheet.Name) >= 0))
            {
                throw new SourceGuidanceFormatException("Source guidance sheets are invalid or not in ordinal order.");
            }

            previousSheet = sheet.Name;
            ValidateSheet(sheet);
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
            if (input is null || string.IsNullOrWhiteSpace(input.Language) ||
                !SourceGuidanceHashing.IsSha256(input.ContentId) || !SourceGuidanceHashing.IsSha256(input.SnapshotId) ||
                !languages.Add(input.Language) ||
                (previousLanguage is not null && string.CompareOrdinal(previousLanguage, input.Language) >= 0))
            {
                throw new SourceGuidanceFormatException("Source guidance inputs are invalid or not in ordinal language order.");
            }

            previousLanguage = input.Language;
        }
    }

    private static void ValidateSheet(SourceGuidanceSheet sheet)
    {
        if (!Enum.IsDefined(sheet.Status) || sheet.Translatable is null || sheet.IncompatibilityReasons is null ||
            sheet.Status == SourceGuidanceSheetStatus.Compatible && sheet.IncompatibilityReasons.Count != 0 ||
            sheet.Status == SourceGuidanceSheetStatus.Incompatible &&
            (sheet.IncompatibilityReasons.Count == 0 || sheet.Translatable.Count != 0) ||
            sheet.IncompatibilityReasons.Distinct().Count() != sheet.IncompatibilityReasons.Count)
        {
            throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' is invalid.");
        }

        int previousReason = -1;
        foreach (SourceGuidanceIncompatibilityReason reason in sheet.IncompatibilityReasons)
        {
            if (!Enum.IsDefined(reason) || (int)reason <= previousReason)
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has invalid incompatibility reasons.");
            }

            previousReason = (int)reason;
        }

        uint previousRowId = 0;
        ushort previousSubrowId = 0;
        int previousColumnIndex = -1;
        bool hasPreviousOccurrence = false;
        foreach (SourceGuidanceOccurrence occurrence in sheet.Translatable)
        {
            if (occurrence is null || occurrence.ColumnIndex < 0 ||
                (hasPreviousOccurrence && CompareOccurrences(previousRowId, previousSubrowId, previousColumnIndex, occurrence) >= 0))
            {
                throw new SourceGuidanceFormatException($"Source guidance sheet '{sheet.Name}' has invalid translatable coordinates.");
            }

            previousRowId = occurrence.RowId;
            previousSubrowId = occurrence.SubrowId;
            previousColumnIndex = occurrence.ColumnIndex;
            hasPreviousOccurrence = true;
        }
    }

    private static int CompareOccurrences(
        uint rowId,
        ushort subrowId,
        int columnIndex,
        SourceGuidanceOccurrence candidate)
    {
        int rowComparison = rowId.CompareTo(candidate.RowId);
        if (rowComparison != 0)
        {
            return rowComparison;
        }

        int subrowComparison = subrowId.CompareTo(candidate.SubrowId);
        return subrowComparison != 0 ? subrowComparison : columnIndex.CompareTo(candidate.ColumnIndex);
    }
}
