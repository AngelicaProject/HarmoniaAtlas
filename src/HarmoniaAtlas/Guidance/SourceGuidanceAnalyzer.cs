using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Guidance;

public sealed class SourceGuidanceAnalyzer
{
    private const string TextNamespacePrefix = "TEXT_";

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
                new SourceGuidanceEligibility(1, guidanceSheets),
                null);
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
        string[] languages = inputs.Select(input => input.Verified.Metadata.Language).ToArray();
        if (reasons.Count != 0)
        {
            return IncompatibleSheet(sheetName, schemaHash, representative.Columns, languages, reasons);
        }

        List<HarmoniaColumnDefinition> stringColumns = representative.Columns
            .Where(column => column.Type == HarmoniaColumnType.String)
            .OrderBy(column => column.Index)
            .ToList();
        if (!TryAnalyzeRows(sheetName, stringColumns, inputs, out List<ColumnAccumulator> accumulators))
        {
            reasons.Add(SourceGuidanceIncompatibilityReason.RowTopologyMismatch);
            return IncompatibleSheet(sheetName, schemaHash, representative.Columns, languages, reasons);
        }

        SourceGuidanceColumn[] columns = stringColumns
            .Select((column, index) => accumulators[index].CreateColumn(column.Index, languages))
            .ToArray();
        return new SourceGuidanceSheet(
            sheetName,
            SourceGuidanceSheetStatus.Compatible,
            schemaHash,
            columns,
            Array.Empty<SourceGuidanceIncompatibilityReason>());
    }

    private static SourceGuidanceSheet IncompatibleSheet(
        string sheetName,
        string schemaHash,
        IReadOnlyList<HarmoniaColumnDefinition> columns,
        IReadOnlyList<string> languages,
        IReadOnlyList<SourceGuidanceIncompatibilityReason> reasons)
    {
        SourceGuidanceColumn[] guidanceColumns = columns
            .Where(column => column.Type == HarmoniaColumnType.String)
            .OrderBy(column => column.Index)
            .Select(column => new SourceGuidanceColumn(
                column.Index,
                SourceGuidanceRole.Unknown,
                new SourceGuidanceEvidence(
                    SourceGuidanceEvidenceKind.IncompatibleSourceLayout,
                    languages,
                    0,
                    0)))
            .ToArray();
        return new SourceGuidanceSheet(
            sheetName,
            SourceGuidanceSheetStatus.Incompatible,
            schemaHash,
            guidanceColumns,
            reasons);
    }

    private static bool TryAnalyzeRows(
        string sheetName,
        IReadOnlyList<HarmoniaColumnDefinition> stringColumns,
        IReadOnlyList<OpenInput> inputs,
        out List<ColumnAccumulator> accumulators)
    {
        accumulators = stringColumns.Select(_ => new ColumnAccumulator()).ToList();
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
                for (int columnIndex = 0; columnIndex < stringColumns.Count; columnIndex++)
                {
                    int expectedColumnIndex = stringColumns[columnIndex].Index;
                    string[] macroTexts = new string[currentRows.Length];
                    for (int inputIndex = 0; inputIndex < currentRows.Length; inputIndex++)
                    {
                        HxsStringRowRecord row = currentRows[inputIndex]!;
                        int cellIndex = cellIndexes[inputIndex];
                        if (cellIndex >= row.StringCells.Count || row.StringCells[cellIndex].ColumnIndex != expectedColumnIndex)
                        {
                            throw new SourceGuidanceException(
                                $"Verified HXS sheet '{sheetName}' does not have canonical String-cell coverage at column {expectedColumnIndex}.");
                        }

                        macroTexts[inputIndex] = row.StringCells[cellIndex].MacroText;
                        cellIndexes[inputIndex] = cellIndex + 1;
                    }

                    accumulators[columnIndex].Observe(macroTexts);
                }

                if (Enumerable.Range(0, currentRows.Length).Any(index => cellIndexes[index] != currentRows[index]!.StringCells.Count))
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

    private sealed class ColumnAccumulator
    {
        private bool _hasNonEmptyValue;
        private bool _allNonEmptyValuesUseTextNamespace = true;
        private long _comparableOccurrences;
        private long _varyingOccurrences;

        public void Observe(IReadOnlyList<string> macroTexts)
        {
            _comparableOccurrences = checked(_comparableOccurrences + 1);
            string first = macroTexts[0];
            bool varying = macroTexts.Skip(1).Any(value => !string.Equals(first, value, StringComparison.Ordinal));
            if (varying)
            {
                _varyingOccurrences = checked(_varyingOccurrences + 1);
            }

            foreach (string macroText in macroTexts)
            {
                if (macroText.Length == 0)
                {
                    continue;
                }

                _hasNonEmptyValue = true;
                if (!macroText.StartsWith(TextNamespacePrefix, StringComparison.Ordinal))
                {
                    _allNonEmptyValuesUseTextNamespace = false;
                }
            }
        }

        public SourceGuidanceColumn CreateColumn(int columnIndex, IReadOnlyList<string> languages)
        {
            if (_hasNonEmptyValue && _allNonEmptyValuesUseTextNamespace)
            {
                return new SourceGuidanceColumn(
                    columnIndex,
                    SourceGuidanceRole.Context,
                    new SourceGuidanceEvidence(
                        SourceGuidanceEvidenceKind.KnownTechnicalNamespace,
                        languages,
                        0,
                        0,
                        TextNamespacePrefix));
            }

            if (_varyingOccurrences > 0)
            {
                return new SourceGuidanceColumn(
                    columnIndex,
                    SourceGuidanceRole.Translatable,
                    new SourceGuidanceEvidence(
                        SourceGuidanceEvidenceKind.OfficialLanguageVariance,
                        languages,
                        _comparableOccurrences,
                        _varyingOccurrences));
            }

            return new SourceGuidanceColumn(
                columnIndex,
                SourceGuidanceRole.Unknown,
                new SourceGuidanceEvidence(
                    SourceGuidanceEvidenceKind.NoOfficialLanguageVariance,
                    languages,
                    _comparableOccurrences,
                    0));
        }
    }
}
