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

        List<VerifiedInput> verifiedInputs = normalizedPaths.Select(VerifyInput).ToList();
        VerifiedInput sourceInput = verifiedInputs[0];
        if (verifiedInputs.Select(input => input.Metadata.Language).Distinct(StringComparer.Ordinal).Count() != verifiedInputs.Count)
        {
            throw new SourceGuidanceException("Source guidance HXS languages must be distinct.");
        }

        foreach (VerifiedInput input in verifiedInputs.Skip(1))
        {
            if (!string.Equals(sourceInput.Metadata.GameVersion, input.Metadata.GameVersion, StringComparison.Ordinal))
            {
                throw new SourceGuidanceException(
                    $"Source guidance HXS inputs must use the same game_version; '{sourceInput.Metadata.GameVersion}' and '{input.Metadata.GameVersion}' differ.");
            }

            if (!string.Equals(sourceInput.Metadata.Scope, input.Metadata.Scope, StringComparison.Ordinal))
            {
                throw new SourceGuidanceException(
                    $"Source guidance HXS inputs must use the same scope; '{sourceInput.Metadata.Scope}' and '{input.Metadata.Scope}' differ.");
            }
        }

        List<VerifiedInput> orderedInputs = verifiedInputs
            .OrderBy(input => input.Metadata.Language, StringComparer.Ordinal)
            .ToList();

        List<OpenInput> openInputs = new();
        try
        {
            foreach (VerifiedInput input in orderedInputs)
            {
                HxsReader reader = HxsReader.OpenReadOnly(input.Path);
                try
                {
                    IReadOnlyList<HxsSheetRecord> sheets = reader.ReadSheets();
                    IReadOnlyDictionary<string, HxsSheetRecord> sheetsByName = sheets.ToDictionary(sheet => sheet.Name, StringComparer.Ordinal);
                    string evidenceId = ComputeEvidenceId(input, reader, sheets);
                    openInputs.Add(new OpenInput(input, reader, sheetsByName, evidenceId));
                }
                catch
                {
                    reader.Dispose();
                    throw;
                }
            }

            OpenInput sourceOpenInput = openInputs.Single(input =>
                pathComparer.Equals(input.Verified.Path, sourceInput.Path));
            SourceGuidanceSheet[] guidanceSheets = openInputs
                .SelectMany(input => input.Sheets.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(sheetName => AnalyzeSheet(sheetName, sourceOpenInput, openInputs))
                .ToArray();

            SourceGuidanceSourceIdentity sourceIdentity = new(
                sourceInput.Metadata.Language,
                sourceInput.Metadata.ContentId,
                sourceInput.Metadata.SnapshotId);
            SourceGuidanceEvidenceInput[] evidenceInputs = orderedInputs
                .Select(input =>
                {
                    OpenInput openInput = openInputs.Single(candidate =>
                        pathComparer.Equals(candidate.Verified.Path, input.Path));
                    return new SourceGuidanceEvidenceInput(input.Metadata.Language, openInput.EvidenceId);
                })
                .ToArray();
            SourceGuidanceBundle withoutBundleId = new(
                1,
                sourceInput.Metadata.GameVersion,
                sourceInput.Metadata.Scope,
                string.Empty,
                sourceIdentity,
                evidenceInputs,
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
            if (!SourceGuidanceLanguages.IsCanonicalSourceLanguage(verification.Metadata.Language))
            {
                throw new SourceGuidanceException(
                    $"Source guidance input '{path}' uses unsupported hxs_meta.language '{verification.Metadata.Language}'. " +
                    "Expected one of: en, ja, de, fr, zh-cn, zh-tw, ko.");
            }

            return new VerifiedInput(path, verification.Metadata);
        }
        catch (SourceGuidanceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SourceGuidanceException($"HXS input '{path}' failed full verification: {exception.Message}", exception);
        }
    }

    private static string ComputeEvidenceId(
        VerifiedInput input,
        HxsReader reader,
        IReadOnlyList<HxsSheetRecord> sheets)
    {
        using SourceGuidanceEvidenceHasher hasher = new(
            input.Metadata.GameVersion,
            input.Metadata.Scope,
            input.Metadata.Language);
        foreach (HxsSheetRecord sheet in sheets.OrderBy(sheet => sheet.Name, StringComparer.Ordinal))
        {
            hasher.AddSheet(sheet.Name, sheet.Variant, sheet.SchemaHash);
            foreach (HxsStringRowRecord row in reader.ReadStringRows(sheet.Name))
            {
                hasher.AddRow(row.RowId, row.SubrowId);
                foreach (HxsStringOccurrenceValue value in row.Values)
                {
                    hasher.AddStringOccurrence(value.ColumnIndex, value.MacroText);
                }
            }
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
        HxsSheetRecord representative = representativeInput.Sheets[sheetName];
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

            HxsSheetRecord sheet = input.Sheets[sheetName];
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
        IReadOnlyDictionary<string, HxsSheetRecord> Sheets,
        string EvidenceId);
}
