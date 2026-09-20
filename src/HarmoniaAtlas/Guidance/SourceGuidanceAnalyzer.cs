using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Guidance;

public sealed class SourceGuidanceAnalyzer
{
    public SourceGuidanceBundle Analyze(string sourcePath, IReadOnlyList<string> comparePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(comparePaths);
        if (comparePaths.Count == 0)
        {
            throw new SourceGuidanceException("Source guidance requires at least one comparison HXS input.");
        }

        List<string> normalizedPaths = new[] { sourcePath }
            .Concat(comparePaths)
            .Select(Path.GetFullPath)
            .ToList();
        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (normalizedPaths.Count != normalizedPaths.Distinct(pathComparer).Count())
        {
            throw new SourceGuidanceException("Source guidance source and comparison paths must be distinct.");
        }

        List<HxsGuidanceEvidenceSource> inputs = new();
        try
        {
            foreach (string normalizedPath in normalizedPaths)
            {
                inputs.Add(HxsGuidanceEvidenceSource.OpenVerified(normalizedPath));
            }

            return Analyze(inputs[0], inputs.Skip(1).Cast<IGuidanceEvidenceSource>().ToArray());
        }
        finally
        {
            foreach (HxsGuidanceEvidenceSource input in inputs.AsEnumerable().Reverse())
            {
                input.Dispose();
            }
        }
    }

    public SourceGuidanceBundle Analyze(
        IGuidanceEvidenceSource source,
        IReadOnlyList<IGuidanceEvidenceSource> compareInputs,
        Action<GuidanceScanProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compareInputs);
        if (compareInputs.Count == 0)
        {
            throw new SourceGuidanceException("Source guidance requires at least one comparison input.");
        }

        List<IGuidanceEvidenceSource> inputs = new[] { source }.Concat(compareInputs).ToList();
        ValidateInputs(inputs);
        List<OpenInput> openInputs = inputs
            .OrderBy(input => input.Language, StringComparer.Ordinal)
            .Select(input => new OpenInput(input, input.Sheets, ComputeEvidenceId(input, progress)))
            .ToList();
        OpenInput sourceOpenInput = openInputs.Single(input => ReferenceEquals(input.Source, source));

        SourceGuidanceSheet[] guidanceSheets = openInputs
            .SelectMany(input => input.Sheets.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(sheetName => AnalyzeSheet(sheetName, sourceOpenInput, openInputs))
            .ToArray();

        if (source.ContentId is null || source.SnapshotId is null)
        {
            throw new SourceGuidanceException("The guidance source must expose a verified HXS contentId and snapshotId.");
        }

        SourceGuidanceBundle withoutBundleId = new(
            1,
            source.GameVersion,
            source.Scope,
            string.Empty,
            new SourceGuidanceSourceIdentity(source.Language, source.ContentId, source.SnapshotId),
            openInputs.Select(input => new SourceGuidanceEvidenceInput(input.Source.Language, input.EvidenceId)).ToArray(),
            guidanceSheets);
        return withoutBundleId with { BundleId = SourceGuidanceHashing.ComputeBundleId(withoutBundleId) };
    }

    private static void ValidateInputs(IReadOnlyList<IGuidanceEvidenceSource> inputs)
    {
        if (inputs.Select(input => input.Language).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
        {
            throw new SourceGuidanceException("Source guidance inputs must use distinct languages.");
        }

        IGuidanceEvidenceSource source = inputs[0];
        if (!SourceGuidanceLanguages.IsCanonicalSourceLanguage(source.Language))
        {
            throw new SourceGuidanceException($"Source guidance source uses unsupported language '{source.Language}'.");
        }

        foreach (IGuidanceEvidenceSource input in inputs)
        {
            if (!SourceGuidanceLanguages.IsCanonicalSourceLanguage(input.Language))
            {
                throw new SourceGuidanceException($"Source guidance input uses unsupported language '{input.Language}'.");
            }

            if (!string.Equals(source.GameVersion, input.GameVersion, StringComparison.Ordinal) ||
                !string.Equals(source.Scope, input.Scope, StringComparison.Ordinal))
            {
                throw new SourceGuidanceException("Source guidance inputs must use the same game version and scope.");
            }
        }
    }

    private static string ComputeEvidenceId(IGuidanceEvidenceSource input, Action<GuidanceScanProgress>? progress)
    {
        using SourceGuidanceEvidenceHasher hasher = new(input.GameVersion, input.Scope, input.Language);
        string[] sheetNames = input.Sheets.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        for (int sheetIndex = 0; sheetIndex < sheetNames.Length; sheetIndex++)
        {
            string sheetName = sheetNames[sheetIndex];
            GuidanceSheetMetadata sheet = input.Sheets[sheetName];
            hasher.AddSheet(sheet.Name, sheet.Variant, sheet.SchemaHash);
            long rowsProcessed = 0;
            long lastProgressTimestamp = Environment.TickCount64;
            foreach (GuidanceStringRow row in input.ReadStringRows(sheetName))
            {
                hasher.AddRow(row.RowId, row.SubrowId);
                foreach (GuidanceStringValue value in row.Values.OrderBy(value => value.ColumnIndex))
                {
                    hasher.AddStringOccurrence(value.ColumnIndex, value.MacroText);
                }

                rowsProcessed++;
                if (progress is not null &&
                    (rowsProcessed % 1000 == 0 || Environment.TickCount64 - lastProgressTimestamp >= 250))
                {
                    progress(new GuidanceScanProgress(input.Language, sheetName, sheetIndex + 1, sheetNames.Length, rowsProcessed));
                    lastProgressTimestamp = Environment.TickCount64;
                }
            }

            progress?.Invoke(new GuidanceScanProgress(input.Language, sheetName, sheetIndex + 1, sheetNames.Length, rowsProcessed));
        }

        return hasher.ComputeEvidenceId();
    }

    private static SourceGuidanceSheet AnalyzeSheet(
        string sheetName,
        OpenInput sourceInput,
        IReadOnlyList<OpenInput> inputs)
    {
        List<OpenInput> presentInputs = inputs.Where(input => input.Sheets.ContainsKey(sheetName)).ToList();
        OpenInput representativeInput = sourceInput.Sheets.ContainsKey(sheetName) ? sourceInput : presentInputs[0];
        GuidanceSheetMetadata representative = representativeInput.Sheets[sheetName];
        List<SourceGuidanceIncompatibilityReason> reasons = new();
        if (presentInputs.Count != inputs.Count)
        {
            reasons.Add(SourceGuidanceIncompatibilityReason.MissingInInput);
        }

        foreach (OpenInput input in presentInputs)
        {
            if (ReferenceEquals(input, representativeInput))
            {
                continue;
            }

            GuidanceSheetMetadata sheet = input.Sheets[sheetName];
            if (sheet.Variant != representative.Variant)
            {
                reasons.Add(SourceGuidanceIncompatibilityReason.SheetVariantMismatch);
            }

            if (!ColumnsEqual(representative.Columns, sheet.Columns))
            {
                reasons.Add(SourceGuidanceIncompatibilityReason.ColumnDefinitionMismatch);
            }

            if (!representative.SchemaHash.AsSpan().SequenceEqual(sheet.SchemaHash))
            {
                reasons.Add(SourceGuidanceIncompatibilityReason.SchemaHashMismatch);
            }
        }

        reasons = reasons.Distinct().OrderBy(reason => (int)reason).ToList();
        string schemaHash = SourceGuidanceHashing.ToHashString(representative.SchemaHash);
        if (reasons.Count != 0)
        {
            return IncompatibleSheet(sheetName, schemaHash, reasons);
        }

        if (!TryAnalyzeRows(sheetName, representative.Columns, inputs, out List<SourceGuidanceOccurrence> translatable))
        {
            reasons.Add(SourceGuidanceIncompatibilityReason.RowTopologyMismatch);
            return IncompatibleSheet(sheetName, schemaHash, reasons);
        }

        return new SourceGuidanceSheet(
            sheetName,
            schemaHash,
            SourceGuidanceSheetStatus.Compatible,
            translatable,
            Array.Empty<SourceGuidanceIncompatibilityReason>());
    }

    private static SourceGuidanceSheet IncompatibleSheet(
        string sheetName,
        string schemaHash,
        IReadOnlyList<SourceGuidanceIncompatibilityReason> reasons) =>
        new(sheetName, schemaHash, SourceGuidanceSheetStatus.Incompatible, Array.Empty<SourceGuidanceOccurrence>(), reasons);

    private static bool TryAnalyzeRows(
        string sheetName,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        IReadOnlyList<OpenInput> inputs,
        out List<SourceGuidanceOccurrence> translatable)
    {
        translatable = new();
        int[] stringColumnIndexes = columns
            .Where(column => column.Type == HarmoniaColumnType.String)
            .Select(column => column.Index)
            .OrderBy(index => index)
            .ToArray();
        List<IEnumerator<GuidanceStringRow>> enumerators = inputs
            .Select(input => input.Source.ReadStringRows(sheetName).GetEnumerator())
            .ToList();
        try
        {
            while (true)
            {
                GuidanceStringRow?[] currentRows = new GuidanceStringRow?[enumerators.Count];
                bool anyMoved = false;
                bool allMoved = true;
                for (int index = 0; index < enumerators.Count; index++)
                {
                    bool moved = enumerators[index].MoveNext();
                    anyMoved |= moved;
                    allMoved &= moved;
                    if (moved)
                    {
                        currentRows[index] = enumerators[index].Current;
                    }
                }

                if (!anyMoved)
                {
                    return true;
                }

                if (!allMoved || currentRows.Any(row => row is null) ||
                    currentRows.Any(row => row!.RowId != currentRows[0]!.RowId || row.SubrowId != currentRows[0]!.SubrowId))
                {
                    return false;
                }

                int[] cellIndexes = new int[currentRows.Length];
                foreach (int columnIndex in stringColumnIndexes)
                {
                    string[] macroTexts = new string[currentRows.Length];
                    for (int inputIndex = 0; inputIndex < currentRows.Length; inputIndex++)
                    {
                        GuidanceStringRow row = currentRows[inputIndex]!;
                        int cellIndex = cellIndexes[inputIndex];
                        if (cellIndex >= row.Values.Count || row.Values[cellIndex].ColumnIndex != columnIndex)
                        {
                            throw new SourceGuidanceException(
                                $"Guidance sheet '{sheetName}' does not have canonical String-cell coverage at column {columnIndex}.");
                        }

                        macroTexts[inputIndex] = row.Values[cellIndex].MacroText;
                        cellIndexes[inputIndex] = cellIndex + 1;
                    }

                    if (macroTexts.Skip(1).Any(value => !string.Equals(macroTexts[0], value, StringComparison.Ordinal)))
                    {
                        translatable.Add(new SourceGuidanceOccurrence(currentRows[0]!.RowId, currentRows[0]!.SubrowId, columnIndex));
                    }
                }

                if (Enumerable.Range(0, currentRows.Length).Any(index => cellIndexes[index] != currentRows[index]!.Values.Count))
                {
                    throw new SourceGuidanceException($"Guidance sheet '{sheetName}' contains an unexpected String value.");
                }
            }
        }
        finally
        {
            foreach (IEnumerator<GuidanceStringRow> enumerator in enumerators)
            {
                enumerator.Dispose();
            }
        }
    }

    private static bool ColumnsEqual(IReadOnlyList<HarmoniaColumnDefinition> first, IReadOnlyList<HarmoniaColumnDefinition> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.Offset == pair.Second.Offset &&
            pair.First.Type == pair.Second.Type);

    private sealed record OpenInput(
        IGuidanceEvidenceSource Source,
        IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets,
        string EvidenceId);
}
