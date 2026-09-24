using HarmoniaAtlas.Game;
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
            .Select(input => new OpenInput(input))
            .ToList();
        // A sheet that no input could read has no schema to report and grants
        // nothing, so only sheets present in at least one input are listed.
        string[] sheetNames = openInputs
            .SelectMany(input => input.Sheets.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        int sourceIndex = openInputs.FindIndex(input => ReferenceEquals(input.Source, source));
        Dictionary<string, SourceGuidanceSheet> guidanceSheets = new(StringComparer.Ordinal);

        try
        {
            for (int sheetIndex = 0; sheetIndex < sheetNames.Length; sheetIndex++)
            {
                string sheetName = sheetNames[sheetIndex];
                List<OpenInput> presentInputs = openInputs.Where(input => input.Sheets.ContainsKey(sheetName)).ToList();
                SheetAnalysis analysis = AnalyzeMetadata(sheetName, openInputs, presentInputs);
                foreach (OpenInput input in presentInputs)
                {
                    input.Hasher.AddSheet(sheetName, input.Sheets[sheetName].Variant, input.Sheets[sheetName].SchemaHash);
                }

                if (analysis.Reasons.Count != 0)
                {
                    foreach (OpenInput input in presentInputs)
                    {
                        ScanSingle(input, sheetName, sheetIndex, sheetNames.Length, progress);
                    }

                    guidanceSheets.Add(sheetName, IncompatibleSheet(analysis.Sheet, analysis.Reasons));
                    continue;
                }

                ScanResult scan = ScanComparable(
                    sheetName,
                    analysis.Sheet.Columns,
                    presentInputs,
                    presentInputs.IndexOf(openInputs[sourceIndex]),
                    sheetIndex,
                    sheetNames.Length,
                    progress,
                    out List<SourceGuidanceOccurrence> translatable);
                guidanceSheets.Add(sheetName, scan switch
                {
                    ScanResult.Compatible => new SourceGuidanceSheet(
                        sheetName,
                        SourceGuidanceHashing.ToHashString(analysis.Sheet.SchemaHash),
                        SourceGuidanceSheetStatus.Compatible,
                        translatable,
                        Array.Empty<SourceGuidanceIncompatibilityReason>()),
                    ScanResult.TopologyMismatch => IncompatibleSheet(analysis.Sheet, [SourceGuidanceIncompatibilityReason.RowTopologyMismatch]),
                    _ => IncompatibleSheet(analysis.Sheet, [SourceGuidanceIncompatibilityReason.UnreadableInInput]),
                });
            }

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
                openInputs.Select(input => new SourceGuidanceEvidenceInput(input.Source.Language, input.Hasher.ComputeEvidenceId())).ToArray(),
                sheetNames.Select(sheetName => guidanceSheets[sheetName]).ToArray());
            return withoutBundleId with { BundleId = SourceGuidanceHashing.ComputeBundleId(withoutBundleId) };
        }
        finally
        {
            foreach (OpenInput input in openInputs)
            {
                input.Hasher.Dispose();
            }
        }
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

    private static SheetAnalysis AnalyzeMetadata(
        string sheetName,
        IReadOnlyList<OpenInput> allInputs,
        IReadOnlyList<OpenInput> presentInputs)
    {
        OpenInput representativeInput = allInputs[0].Sheets.ContainsKey(sheetName)
            ? allInputs[0]
            : presentInputs[0];
        GuidanceSheetMetadata representative = representativeInput.Sheets[sheetName];
        List<SourceGuidanceIncompatibilityReason> reasons = new();
        foreach (OpenInput input in allInputs.Where(input => !input.Sheets.ContainsKey(sheetName)))
        {
            reasons.Add(input.Source.UnreadableSheets.Contains(sheetName)
                ? SourceGuidanceIncompatibilityReason.UnreadableInInput
                : SourceGuidanceIncompatibilityReason.MissingInInput);
        }

        foreach (OpenInput input in presentInputs)
        {
            GuidanceSheetMetadata sheet = input.Sheets[sheetName];
            if (!sheet.LanguageSafe)
            {
                // HSG v1 has no dedicated fallback reason. RowTopologyMismatch is the existing
                // fail-closed incompatibility state and keeps the persisted contract unchanged.
                reasons.Add(SourceGuidanceIncompatibilityReason.RowTopologyMismatch);
            }

            if (ReferenceEquals(input, representativeInput))
            {
                continue;
            }

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

        return new SheetAnalysis(
            representative,
            reasons.Distinct().OrderBy(reason => (int)reason).ToArray());
    }

    private static void ScanSingle(
        OpenInput input,
        string sheetName,
        int sheetIndex,
        int sheetCount,
        Action<GuidanceScanProgress>? progress)
    {
        long rowsProcessed = 0;
        long lastProgressTimestamp = Environment.TickCount64;
        try
        {
            foreach (GuidanceStringRow row in input.Source.ReadStringRows(sheetName))
            {
                AddEvidenceRow(input.Hasher, row);
                rowsProcessed++;
                EmitProgress(input, sheetName, sheetIndex, sheetCount, rowsProcessed, ref lastProgressTimestamp, progress);
            }
        }
        catch (SheetReadException)
        {
            // The sheet is already incompatible; its evidence ends where the
            // input stopped being readable.
        }

        progress?.Invoke(new GuidanceScanProgress(input.Source.Language, sheetName, sheetIndex + 1, sheetCount, rowsProcessed));
    }

    /// <summary>
    /// Compares one compatible sheet row by row. An occurrence is translatable
    /// when its source text is not empty and at least one input differs from
    /// the others.
    /// </summary>
    private static ScanResult ScanComparable(
        string sheetName,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        IReadOnlyList<OpenInput> inputs,
        int sourceIndex,
        int sheetIndex,
        int sheetCount,
        Action<GuidanceScanProgress>? progress,
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
        long[] rowsProcessed = new long[inputs.Count];
        long[] lastProgressTimestamps = Enumerable.Repeat(Environment.TickCount64, inputs.Count).ToArray();
        bool topologyIsCompatible = true;
        bool readable = true;
        try
        {
            while (readable)
            {
                GuidanceStringRow?[] currentRows = new GuidanceStringRow?[inputs.Count];
                bool anyMoved = false;
                bool allMoved = true;
                for (int index = 0; index < enumerators.Count; index++)
                {
                    bool moved;
                    try
                    {
                        moved = enumerators[index].MoveNext();
                    }
                    catch (SheetReadException)
                    {
                        readable = false;
                        break;
                    }

                    anyMoved |= moved;
                    allMoved &= moved;
                    if (moved)
                    {
                        currentRows[index] = enumerators[index].Current;
                        rowsProcessed[index]++;
                    }
                }

                if (!readable || !anyMoved)
                {
                    break;
                }

                bool coordinatesMatch = allMoved && currentRows.All(row => row is not null) &&
                    currentRows.All(row => row!.RowId == currentRows[0]!.RowId && row.SubrowId == currentRows[0]!.SubrowId);
                if (!coordinatesMatch)
                {
                    topologyIsCompatible = false;
                }

                for (int index = 0; index < inputs.Count; index++)
                {
                    if (currentRows[index] is not null)
                    {
                        AddEvidenceRow(inputs[index].Hasher, currentRows[index]!);
                        EmitProgress(inputs[index], sheetName, sheetIndex, sheetCount, rowsProcessed[index], ref lastProgressTimestamps[index], progress);
                    }
                }

                if (!coordinatesMatch)
                {
                    continue;
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

                    if (topologyIsCompatible &&
                        macroTexts[sourceIndex].Length != 0 &&
                        macroTexts.Skip(1).Any(value => !string.Equals(macroTexts[0], value, StringComparison.Ordinal)))
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

        for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            OpenInput input = inputs[inputIndex];
            progress?.Invoke(new GuidanceScanProgress(input.Source.Language, sheetName, sheetIndex + 1, sheetCount, rowsProcessed[inputIndex]));
        }

        if (!readable || !topologyIsCompatible)
        {
            translatable.Clear();
        }

        return !readable
            ? ScanResult.Unreadable
            : topologyIsCompatible ? ScanResult.Compatible : ScanResult.TopologyMismatch;
    }

    private enum ScanResult
    {
        Compatible,
        TopologyMismatch,
        Unreadable,
    }

    private static void AddEvidenceRow(SourceGuidanceEvidenceHasher hasher, GuidanceStringRow row)
    {
        hasher.AddRow(row.RowId, row.SubrowId);
        foreach (GuidanceStringValue value in row.Values.OrderBy(value => value.ColumnIndex))
        {
            hasher.AddStringOccurrence(value.ColumnIndex, value.MacroText);
        }
    }

    private static void EmitProgress(
        OpenInput input,
        string sheetName,
        int sheetIndex,
        int sheetCount,
        long rowsProcessed,
        ref long lastProgressTimestamp,
        Action<GuidanceScanProgress>? progress)
    {
        if (progress is not null &&
            (rowsProcessed % 1000 == 0 || Environment.TickCount64 - lastProgressTimestamp >= 250))
        {
            progress(new GuidanceScanProgress(input.Source.Language, sheetName, sheetIndex + 1, sheetCount, rowsProcessed));
            lastProgressTimestamp = Environment.TickCount64;
        }
    }

    private static SourceGuidanceSheet IncompatibleSheet(
        GuidanceSheetMetadata sheet,
        IReadOnlyList<SourceGuidanceIncompatibilityReason> reasons) =>
        new(
            sheet.Name,
            SourceGuidanceHashing.ToHashString(sheet.SchemaHash),
            SourceGuidanceSheetStatus.Incompatible,
            Array.Empty<SourceGuidanceOccurrence>(),
            reasons);

    private static bool ColumnsEqual(IReadOnlyList<HarmoniaColumnDefinition> first, IReadOnlyList<HarmoniaColumnDefinition> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.Offset == pair.Second.Offset &&
            pair.First.Type == pair.Second.Type);

    private sealed class OpenInput
    {
        public OpenInput(IGuidanceEvidenceSource source)
        {
            Source = source;
            Sheets = source.Sheets;
            Hasher = new SourceGuidanceEvidenceHasher(source.GameVersion, source.Scope, source.Language);
        }

        public IGuidanceEvidenceSource Source { get; }
        public IReadOnlyDictionary<string, GuidanceSheetMetadata> Sheets { get; }
        public SourceGuidanceEvidenceHasher Hasher { get; }
    }

    private sealed record SheetAnalysis(
        GuidanceSheetMetadata Sheet,
        IReadOnlyList<SourceGuidanceIncompatibilityReason> Reasons);
}
