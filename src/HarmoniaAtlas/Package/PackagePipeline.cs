using System.Diagnostics;
using HarmoniaAtlas.Extraction;
using HarmoniaAtlas.Game;
using HarmoniaAtlas.Guidance;

namespace HarmoniaAtlas.Package;

public sealed record PackageEvent(string Type, IReadOnlyDictionary<string, object?> Fields);

public sealed record PackageResult(
    HspPackageSummary Package,
    IReadOnlyList<string> EvidenceLanguages,
    long InspectInstallationMs,
    long ExtractSourceMs,
    long VerifySourceMs,
    long ScanEvidenceMs,
    long WritePackageMs,
    long ValidatePackageMs,
    long ElapsedMs)
{
    public int TranslatableOccurrenceCount => Package.Guidance.Sheets.Sum(sheet => sheet.Translatable.Count);
}

public static class PackageEvidencePolicy
{
    public static readonly IReadOnlyList<string> GlobalLanguages = ["en", "ja", "de", "fr"];

    public static IReadOnlyList<string> For(GameLanguage language)
    {
        string code = language.ToCode();
        if (!GlobalLanguages.Contains(code, StringComparer.Ordinal))
        {
            throw new PackageException(
                $"The package evidence policy currently supports only the global FFXIV language family: {string.Join(", ", GlobalLanguages)}. " +
                $"Language '{code}' cannot safely produce multilingual guidance.");
        }

        return GlobalLanguages;
    }
}

public sealed class PackagePipeline
{
    public PackageResult Generate(
        string gamePath,
        string language,
        string outputPath,
        Action<PackageEvent>? emit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        Stopwatch total = Stopwatch.StartNew();
        string fullOutputPath = Path.GetFullPath(outputPath);
        TryDelete(fullOutputPath + ".partial");
        string temporaryRoot = PackageWorkingState.Prepare(fullOutputPath);
        string sourcePath = Path.Combine(temporaryRoot, "source.hxs");
        string guidancePath = Path.Combine(temporaryRoot, "source-guidance.json");
        string? partialPath = null;

        try
        {
            emit?.Invoke(new PackageEvent("started", new Dictionary<string, object?>
            {
                ["protocolVersion"] = 1,
                ["language"] = language,
            }));

            Stopwatch phase = Stopwatch.StartNew();
            emit?.Invoke(Phase("inspectInstallation"));
            GameInstallation installation = GameInstallation.FromPath(gamePath);
            GameLanguage requestedLanguage = GameLanguageParser.Parse(language);
            IReadOnlyList<string> evidenceLanguages = PackageEvidencePolicy.For(requestedLanguage);
            long inspectMs = phase.ElapsedMilliseconds;

            phase.Restart();
            emit?.Invoke(Phase("extractSource"));
            ExtractionSummary extraction = new ExtractionEngine().Extract(
                installation.FullPath,
                requestedLanguage.ToCode(),
                sourcePath,
                progress => emit?.Invoke(new PackageEvent("progress", new Dictionary<string, object?>
                {
                    ["protocolVersion"] = 1,
                    ["phase"] = "extractSource",
                    ["sheet"] = progress.Sheet,
                    ["sheetIndex"] = progress.SheetIndex,
                    ["sheetCount"] = progress.SheetCount,
                    ["rowsProcessed"] = progress.RowsProcessed,
                    ["sheetCompleted"] = progress.SheetCompleted,
                })));
            long extractMs = phase.ElapsedMilliseconds;

            phase.Restart();
            emit?.Invoke(Phase("verifySource"));
            Hxs.HxsVerificationResult sourceVerification = Hxs.HxsVerifier.Verify(sourcePath);
            long verifyMs = phase.ElapsedMilliseconds;

            phase.Restart();
            emit?.Invoke(Phase("scanEvidence"));
            using HxsGuidanceEvidenceSource sourceEvidence = HxsGuidanceEvidenceSource.OpenReadOnly(sourcePath, sourceVerification);
            List<LuminaGuidanceEvidenceSource> comparisonSources = new();
            try
            {
                List<IGuidanceEvidenceSource> inputs = new();
                foreach (string evidenceLanguage in evidenceLanguages)
                {
                    if (string.Equals(evidenceLanguage, requestedLanguage.ToCode(), StringComparison.Ordinal))
                    {
                        inputs.Add(sourceEvidence);
                        continue;
                    }

                    LuminaGuidanceEvidenceSource comparison = LuminaGuidanceEvidenceSource.Open(installation, GameLanguageParser.Parse(evidenceLanguage));
                    comparisonSources.Add(comparison);
                    inputs.Add(comparison);
                }

                SourceGuidanceSummary guidance = new SourceGuidanceGenerator().Generate(
                    sourceEvidence,
                    inputs.Where(input => !ReferenceEquals(input, sourceEvidence)).ToArray(),
                    guidancePath,
                    progress => emit?.Invoke(new PackageEvent("progress", new Dictionary<string, object?>
                    {
                        ["protocolVersion"] = 1,
                        ["phase"] = "scanEvidence",
                        ["language"] = progress.Language,
                        ["sheet"] = progress.Sheet,
                        ["sheetIndex"] = progress.SheetIndex,
                        ["sheetCount"] = progress.SheetCount,
                        ["rowsProcessed"] = progress.RowsProcessed,
                    })));
                _ = guidance;
            }
            finally
            {
                foreach (LuminaGuidanceEvidenceSource comparison in comparisonSources.AsEnumerable().Reverse())
                {
                    comparison.Dispose();
                }
            }

            long scanMs = phase.ElapsedMilliseconds;

            phase.Restart();
            emit?.Invoke(Phase("writePackage"));
            HspComponentDescriptor guidanceComponent = Component("guidance", "sourceGuidance", "guidance/source-guidance.json", guidancePath);
            HspComponentDescriptor sourceComponent = Component("source", "sourceHxs", "source/source.hxs", sourcePath);
            HspManifest manifestWithoutId = new(
                1,
                string.Empty,
                sourceVerification.Metadata.GameVersion,
                sourceVerification.Metadata.Scope,
                new HspSourceIdentity(
                    sourceVerification.Metadata.Language,
                    sourceVerification.Metadata.ContentId,
                    sourceVerification.Metadata.SnapshotId),
                [guidanceComponent, sourceComponent]);
            HspManifest manifest = manifestWithoutId with { PackageId = HspHashing.ComputePackageId(manifestWithoutId) };
            partialPath = new HspWriter().WritePartial(fullOutputPath, sourcePath, guidancePath, manifest);
            long writeMs = phase.ElapsedMilliseconds;

            phase.Restart();
            emit?.Invoke(Phase("validatePackage"));
            HspPackageSummary validated = HspPackageValidator.Validate(partialPath);
            long validateMs = phase.ElapsedMilliseconds;
            HspWriter.Publish(partialPath, fullOutputPath);
            partialPath = null;
            total.Stop();

            PackageResult result = new(
                validated with { OutputPath = fullOutputPath },
                evidenceLanguages,
                inspectMs,
                extractMs,
                verifyMs,
                scanMs,
                writeMs,
                validateMs,
                total.ElapsedMilliseconds);
            emit?.Invoke(new PackageEvent("completed", new Dictionary<string, object?>
            {
                ["protocolVersion"] = 1,
                ["packageId"] = result.Package.Manifest.PackageId,
                ["outputPath"] = result.Package.OutputPath,
                ["gameVersion"] = result.Package.SourceMetadata.GameVersion,
                ["language"] = result.Package.SourceMetadata.Language,
                ["evidenceLanguages"] = result.EvidenceLanguages,
                ["snapshotId"] = result.Package.SourceMetadata.SnapshotId,
                ["contentId"] = result.Package.SourceMetadata.ContentId,
                ["translatableOccurrenceCount"] = result.TranslatableOccurrenceCount,
                ["inspectInstallationMs"] = result.InspectInstallationMs,
                ["extractSourceMs"] = result.ExtractSourceMs,
                ["verifySourceMs"] = result.VerifySourceMs,
                ["scanEvidenceMs"] = result.ScanEvidenceMs,
                ["writePackageMs"] = result.WritePackageMs,
                ["validatePackageMs"] = result.ValidatePackageMs,
                ["elapsedMs"] = result.ElapsedMs,
            }));
            return result;
        }
        catch
        {
            if (partialPath is not null)
            {
                TryDelete(partialPath);
            }

            TryDelete(fullOutputPath + ".partial");
            throw;
        }
        finally
        {
            PackageWorkingState.Cleanup(temporaryRoot);
        }
    }

    private static PackageEvent Phase(string phase) => new("phase", new Dictionary<string, object?>
    {
        ["protocolVersion"] = 1,
        ["phase"] = phase,
    });

    private static HspComponentDescriptor Component(string id, string kind, string path, string sourcePath)
    {
        FileInfo file = new(sourcePath);
        return new HspComponentDescriptor(id, kind, 1, true, path, file.Length, HspHashing.ComputeFileHash(sourcePath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the generation failure. A partial archive is never valid output.
        }
    }

}

public sealed class PackageException : Exception
{
    public PackageException(string message)
        : base(message)
    {
    }
}
