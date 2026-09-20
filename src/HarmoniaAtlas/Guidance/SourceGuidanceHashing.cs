using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Guidance;

public static class SourceGuidanceHashing
{
    public const string BundleHashDomain = "HARMONIA-SOURCE-GUIDANCE-v1";

    public static string ComputeBundleId(SourceGuidanceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        byte[] hash = CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain(BundleHashDomain);
            hasher.WriteUtf8(bundle.GameVersion);
            hasher.WriteUtf8(bundle.Scope);

            SourceGuidanceInput[] inputs = bundle.Inputs
                .OrderBy(input => input.Language, StringComparer.Ordinal)
                .ToArray();
            hasher.WriteUInt32(checked((uint)inputs.Length));
            foreach (SourceGuidanceInput input in inputs)
            {
                hasher.WriteUtf8(input.Language);
                hasher.WriteUtf8(input.ContentId);
                hasher.WriteUtf8(input.SnapshotId);
            }

            SourceGuidanceSheet[] sheets = bundle.Sheets
                .OrderBy(sheet => sheet.Name, StringComparer.Ordinal)
                .ToArray();
            hasher.WriteUInt32(checked((uint)sheets.Length));
            foreach (SourceGuidanceSheet sheet in sheets)
            {
                hasher.WriteUtf8(sheet.Name);
                hasher.WriteUtf8(ToJsonValue(sheet.Status));
                hasher.WriteUtf8(sheet.SchemaHash);

                SourceGuidanceIncompatibilityReason[] reasons = sheet.IncompatibilityReasons
                    .OrderBy(reason => (int)reason)
                    .ToArray();
                hasher.WriteUInt32(checked((uint)reasons.Length));
                foreach (SourceGuidanceIncompatibilityReason reason in reasons)
                {
                    hasher.WriteUtf8(ToJsonValue(reason));
                }

                SourceGuidanceOccurrence[] occurrences = sheet.Translatable
                    .OrderBy(occurrence => occurrence.RowId)
                    .ThenBy(occurrence => occurrence.SubrowId)
                    .ThenBy(occurrence => occurrence.ColumnIndex)
                    .ToArray();
                hasher.WriteUInt32(checked((uint)occurrences.Length));
                foreach (SourceGuidanceOccurrence occurrence in occurrences)
                {
                    hasher.WriteUInt32(occurrence.RowId);
                    hasher.WriteUInt32(occurrence.SubrowId);
                    hasher.WriteInt32(occurrence.ColumnIndex);
                }
            }
        });

        return ToHashString(hash);
    }

    public static string ToHashString(ReadOnlySpan<byte> hash) =>
        "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

    public static bool IsSha256(string? value)
    {
        if (value is null || value.Length != "sha256:".Length + 64 ||
            !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = "sha256:".Length; index < value.Length; index++)
        {
            char character = value[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ToJsonValue(SourceGuidanceSheetStatus value) => value switch
    {
        SourceGuidanceSheetStatus.Compatible => "compatible",
        SourceGuidanceSheetStatus.Incompatible => "incompatible",
        _ => throw new SourceGuidanceFormatException($"Unsupported guidance sheet status: {value}.")
    };

    private static string ToJsonValue(SourceGuidanceIncompatibilityReason value) => value switch
    {
        SourceGuidanceIncompatibilityReason.MissingInInput => "missingInInput",
        SourceGuidanceIncompatibilityReason.SheetVariantMismatch => "sheetVariantMismatch",
        SourceGuidanceIncompatibilityReason.ColumnDefinitionMismatch => "columnDefinitionMismatch",
        SourceGuidanceIncompatibilityReason.SchemaHashMismatch => "schemaHashMismatch",
        SourceGuidanceIncompatibilityReason.RowTopologyMismatch => "rowTopologyMismatch",
        _ => throw new SourceGuidanceFormatException($"Unsupported guidance incompatibility reason: {value}.")
    };
}
