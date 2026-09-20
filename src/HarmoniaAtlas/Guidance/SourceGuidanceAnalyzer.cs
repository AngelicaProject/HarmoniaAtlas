using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Guidance;

public sealed class SourceGuidanceAnalyzer
{
    public SourceGuidanceBundle Analyze(IReadOnlyList<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        if (inputPaths.Count < 2)
        {
            throw new SourceGuidanceException("Source guidance requires at least two HXS inputs.");
        }

        List<string> normalizedPaths = inputPaths.Select(Path.GetFullPath).ToList();
        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (normalizedPaths.Count != normalizedPaths.Distinct(pathComparer).Count())
        {
            throw new SourceGuidanceException("Source guidance inputs must not contain duplicate paths.");
        }

        List<VerifiedInput> verifiedInputs = normalizedPaths.Select(VerifyInput).ToList();
        if (verifiedInputs.Select(input => input.Metadata.Language).Distinct(StringComparer.Ordinal).Count() != verifiedInputs.Count)
        {
            throw new SourceGuidanceException("Source guidance inputs must have distinct hxs_meta.language values.");
        }

        VerifiedInput first = verifiedInputs[0];
        foreach (VerifiedInput input in verifiedInputs.Skip(1))
        {
            if (!string.Equals(first.Metadata.GameVersion, input.Metadata.GameVersion, StringComparison.Ordinal))
            {
                throw new SourceGuidanceException(
                    $"Source guidance inputs must use the same game_version; '{first.Metadata.GameVersion}' and '{input.Metadata.GameVersion}' differ.");
            }

            if (!string.Equals(first.Metadata.Scope, input.Metadata.Scope, StringComparison.Ordinal))
            {
                throw new SourceGuidanceException(
                    $"Source guidance inputs must use the same scope; '{first.Metadata.Scope}' and '{input.Metadata.Scope}' differ.");
            }
        }

        verifiedInputs = verifiedInputs
            .OrderBy(input => input.Metadata.Language, StringComparer.Ordinal)
            .ToList();

        List<OpenInput> openInputs = new();
        try
        {
            foreach (VerifiedInput input in verifiedInputs)
            {
                HxsReader reader = HxsReader.OpenReadOnly(input.Path);
                try
                {
                    IReadOnlyList<HxsSheetRecord> sheets = reader.ReadSheets();
                    openInputs.Add(new OpenInput(input, reader, sheets.ToDictionary(sheet => sheet.Name, StringComparer.Ordinal)));
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            }

            SourceGuidanceSheet[] guidanceSheets = openInputs
                .SelectMany(input => input.Sheets.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(sheetName => AnalyzeSheet(sheetName, openInputs))
                .ToArray();

            SourceGuidanceInput[] guidanceInputs = verifiedInputs
                .Select(input => new SourceGuidanceInput(input.Metadata.Language, input.Metadata.ContentId, input.Metadata.SnapshotId))
                .ToArray();
            SourceGuidanceBundle withoutBundleId = new(
                1,
                first.Metadata.GameVersion,
                first.Metadata.Scope,
                string.Empty,
                guidanceInputs,
                guidanceSheets);
            return withoutBundleId with { BundleId = SourceGuidanceHashing.ComputeBundleId(withoutBundleId) };
        }
        finally
        {
            foreach (OpenInput input in openInputs.AsEnumerable().Reverse())
            {
                input.Reader.Dispose();
            }
        }
    }

    private static VerifiedInput VerifyInput(string path)
    {
        try
        {
            HxsVerificationResult verification = HxsVerifier.Verify(path);
            return new VerifiedInput(path, verification.Metadata);
        }
        catch (Exception exception)
        {
            throw new SourceGuidanceException($"HXS input '{path}' failed full verification: {exception.Message}", exception);
        }
    }

    private static SourceGuidanceSheet AnalyzeSheet(string sheetName, IReadOnlyList<OpenInput> inputs)
    {
        List<OpenInput> presentInputs = inputs.Where(input => input.Sheets.ContainsKey(sheetName)).ToList();
        HxsSheetRecord representative = presentInputs[0].Sheets[sheetName];
        List<SourceGuidanceIncompatibilityReason> reasons = new();
        if (presentInputs.Count != inputs.Count)
        {
            reasons.Add(SourceGuidanceIncompatibilityReason.MissingInInput);
        }

        foreach (HxsSheetRecord sheet in presentInputs.Skip(1).Select(input => input.Sheets[sheetName]))
        {
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
        new(
            sheetName,
            schemaHash,
            SourceGuidanceSheetStatus.Incompatible,
            Array.Empty<SourceGuidanceOccurrence>(),
            reasons);

    private static bool TryAnalyzeRows(
        string sheetName,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        IReadOnlyList<OpenInput> inputs,
        out List<SourceGuidanceOccurrence> translatable)
    {
        translatable = new();
        List<int> stringColumnIndexes = columns
            .Where(column => column.Type == HarmoniaColumnType.String)
            .Select(column => column.Index)
            .OrderBy(index => index)
            .ToList();
        List<IEnumerator<HxsStringRowRecord>> enumerators = inputs
            .Select(input => input.Reader.ReadStringRows(sheetName).GetEnumerator())
            .ToList();
        try
        {
            while (true)
            {
                HxsStringRowRecord?[] currentRows = new HxsStringRowRecord?[enumerators.Count];
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
                        HxsStringRowRecord row = currentRows[inputIndex]!;
                        int cellIndex = cellIndexes[inputIndex];
                        if (cellIndex >= row.Values.Count || row.Values[cellIndex].ColumnIndex != columnIndex)
                        {
                            throw new SourceGuidanceException(
                                $"Verified HXS sheet '{sheetName}' does not have canonical String-cell coverage at column {columnIndex}.");
                        }

                        macroTexts[inputIndex] = row.Values[cellIndex].MacroText;
                        cellIndexes[inputIndex] = cellIndex + 1;
                    }

                    if (macroTexts.Skip(1).Any(value => !string.Equals(macroTexts[0], value, StringComparison.Ordinal)))
                    {
                        translatable.Add(new SourceGuidanceOccurrence(
                            currentRows[0]!.RowId,
                            currentRows[0]!.SubrowId,
                            columnIndex));
                    }
                }

                if (Enumerable.Range(0, currentRows.Length).Any(index => cellIndexes[index] != currentRows[index]!.Values.Count))
                {
                    throw new SourceGuidanceException($"Verified HXS sheet '{sheetName}' contains an unexpected String cell.");
                }
            }
        }
        finally
        {
            foreach (IEnumerator<HxsStringRowRecord> enumerator in enumerators)
            {
                enumerator.Dispose();
            }
        }
    }

    private static bool ColumnsEqual(IReadOnlyList<HarmoniaColumnDefinition> first, IReadOnlyList<HarmoniaColumnDefinition> second)
    {
        return first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.Offset == pair.Second.Offset &&
            pair.First.Type == pair.Second.Type);
    }

    private sealed record VerifiedInput(string Path, HxsMetadata Metadata);

    private sealed record OpenInput(
        VerifiedInput Verified,
        HxsReader Reader,
        IReadOnlyDictionary<string, HxsSheetRecord> Sheets);
}
