using System.IO.Compression;
using System.Security.Cryptography;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Package;

public static class HspPackageValidator
{
    public static HspPackageSummary Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("HSP package was not found.", fullPath);
        }

        string? temporaryRoot = null;
        try
        {
            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
            ValidateArchiveEntryNames(archive);
            ZipArchiveEntry manifestEntry = archive.Entries.Single(entry => string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal));
            HspManifest manifest = HspJson.DeserializeManifest(ReadAllBytes(manifestEntry));
            ValidateManifest(manifest);

            HashSet<string> expectedEntries = manifest.Components
                .Select(component => component.Path)
                .Append("manifest.json")
                .ToHashSet(StringComparer.Ordinal);
            if (archive.Entries.Count != expectedEntries.Count || archive.Entries.Any(entry => !expectedEntries.Contains(entry.FullName)))
            {
                throw new HspFormatException("HSP archive contains an unlisted or missing entry.");
            }

            temporaryRoot = Path.Combine(Path.GetTempPath(), $"harmonia-atlas-hsp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryRoot);
            Dictionary<string, string> materialized = new(StringComparer.Ordinal);
            foreach (HspComponentDescriptor component in manifest.Components)
            {
                ZipArchiveEntry entry = archive.GetEntry(component.Path)
                    ?? throw new HspFormatException($"HSP component '{component.Id}' is missing from the archive.");
                bool materialize = component.Required && component.Kind is ("sourceHxs" or "sourceGuidance");
                string? materializedPath = materialize ? MaterializedPath(temporaryRoot, component.Path) : null;
                ValidateComponent(entry, component, materializedPath);
                if (materializedPath is not null)
                {
                    materialized[component.Kind] = materializedPath;
                }
            }

            string sourcePath = materialized["sourceHxs"];
            string guidancePath = materialized["sourceGuidance"];
            HxsVerificationResult sourceVerification = HxsVerifier.Verify(sourcePath);
            SourceGuidanceBundle guidance = SourceGuidanceReader.Read(guidancePath);
            using HxsGuidanceEvidenceSource sourceEvidence = HxsGuidanceEvidenceSource.OpenReadOnly(sourcePath, sourceVerification);
            ValidateRelationships(manifest, sourceVerification.Metadata, guidance, sourceEvidence);
            return new HspPackageSummary(manifest, sourceVerification.Metadata, guidance, fullPath);
        }
        catch (HspFormatException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            throw new HspFormatException("HSP archive does not contain both required current components.", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new HspFormatException("HSP archive is not a valid ZIP container.", exception);
        }
        catch (SourceGuidanceException exception)
        {
            throw new HspFormatException($"Embedded source guidance is invalid: {exception.Message}", exception);
        }
        catch (Exception exception)
        {
            throw new HspFormatException($"HSP validation failed: {exception.Message}", exception);
        }
        finally
        {
            if (temporaryRoot is not null)
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }
    }

    public static void ValidateManifest(HspManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != 1 || string.IsNullOrWhiteSpace(manifest.GameVersion) ||
            string.IsNullOrWhiteSpace(manifest.Scope) || manifest.Source is null || manifest.Components is null ||
            !HspHashing.IsSha256(manifest.Source.ContentId) || !HspHashing.IsSha256(manifest.Source.SnapshotId) ||
            !SourceGuidanceLanguageIsCanonical(manifest.Source.Language) || !HspHashing.IsSha256(manifest.PackageId))
        {
            throw new HspFormatException("HSP manifest metadata is invalid.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.Ordinal);
        int sourceCount = 0;
        int guidanceCount = 0;
        foreach (HspComponentDescriptor component in manifest.Components)
        {
            if (component is null || string.IsNullOrWhiteSpace(component.Id) || string.IsNullOrWhiteSpace(component.Kind) ||
                component.FormatVersion <= 0 || component.Size < 0 || !HspHashing.IsSha256(component.Sha256) ||
                !ids.Add(component.Id) || !paths.Add(component.Path) || !IsSafeRelativePath(component.Path))
            {
                throw new HspFormatException("HSP component descriptors are invalid.");
            }

            if (component.Required && component.Kind is not ("sourceHxs" or "sourceGuidance"))
            {
                throw new HspFormatException($"Required HSP component kind '{component.Kind}' is unsupported.");
            }

            if (component.Required && component.Kind == "sourceHxs")
            {
                sourceCount++;
                if (component.FormatVersion != 1 || component.Path != "source/source.hxs")
                {
                    throw new HspFormatException("The required sourceHxs component has an invalid v1 descriptor.");
                }
            }

            if (component.Required && component.Kind == "sourceGuidance")
            {
                guidanceCount++;
                if (component.FormatVersion != 1 || component.Path != "guidance/source-guidance.json")
                {
                    throw new HspFormatException("The required sourceGuidance component has an invalid v1 descriptor.");
                }
            }
        }

        if (sourceCount != 1 || guidanceCount != 1)
        {
            throw new HspFormatException("HSP v1 requires exactly one required sourceHxs and one required sourceGuidance component.");
        }

        HspManifest withoutPackageId = manifest with { PackageId = string.Empty };
        string expectedPackageId = HspHashing.ComputePackageId(withoutPackageId);
        if (!string.Equals(manifest.PackageId, expectedPackageId, StringComparison.Ordinal))
        {
            throw new HspFormatException("HSP packageId does not match the canonical logical manifest content.");
        }
    }

    private static void ValidateArchiveEntryNames(ZipArchive archive)
    {
        if (archive.Entries.Count(entry => string.Equals(entry.FullName, "manifest.json", StringComparison.Ordinal)) != 1 ||
            archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
        {
            throw new HspFormatException("HSP archive must contain one manifest.json and no duplicate entry names.");
        }

        if (archive.Entries.Any(entry => entry.FullName.EndsWith("/", StringComparison.Ordinal)))
        {
            throw new HspFormatException("HSP archive must not contain directory entries.");
        }
    }

    private static void ValidateComponent(ZipArchiveEntry entry, HspComponentDescriptor component, string? materializedPath)
    {
        using Stream input = entry.Open();
        IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        FileStream? output = materializedPath is null
            ? null
            : new FileStream(materializedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                size = checked(size + read);
                hasher.AppendData(buffer, 0, read);
                output?.Write(buffer, 0, read);
            }

            string hash = HspHashing.ToHashString(hasher.GetHashAndReset());
            if (size != component.Size || !string.Equals(hash, component.Sha256, StringComparison.Ordinal))
            {
                throw new HspFormatException($"HSP component '{component.Id}' size or SHA-256 does not match its manifest.");
            }
        }
        finally
        {
            output?.Dispose();
            hasher.Dispose();
        }
    }

    private static void ValidateRelationships(
        HspManifest manifest,
        HxsMetadata source,
        SourceGuidanceBundle guidance,
        HxsGuidanceEvidenceSource sourceEvidence)
    {
        if (!string.Equals(manifest.GameVersion, source.GameVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.Scope, source.Scope, StringComparison.Ordinal) ||
            !string.Equals(manifest.Source.Language, source.Language, StringComparison.Ordinal) ||
            !string.Equals(manifest.Source.ContentId, source.ContentId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Source.SnapshotId, source.SnapshotId, StringComparison.Ordinal))
        {
            throw new HspFormatException("HSP manifest source identity does not match the embedded HXS metadata.");
        }

        if (!string.Equals(guidance.GameVersion, source.GameVersion, StringComparison.Ordinal) ||
            !string.Equals(guidance.Scope, source.Scope, StringComparison.Ordinal) ||
            !string.Equals(guidance.Source.Language, source.Language, StringComparison.Ordinal) ||
            !string.Equals(guidance.Source.ContentId, source.ContentId, StringComparison.Ordinal) ||
            !string.Equals(guidance.Source.SnapshotId, source.SnapshotId, StringComparison.Ordinal))
        {
            throw new HspFormatException("Embedded source guidance does not apply to the embedded source HXS.");
        }

        foreach (SourceGuidanceSheet guidanceSheet in guidance.Sheets.Where(sheet => sheet.Status == SourceGuidanceSheetStatus.Compatible))
        {
            if (!sourceEvidence.Sheets.TryGetValue(guidanceSheet.Name, out GuidanceSheetMetadata? sourceSheet) ||
                !string.Equals(guidanceSheet.SchemaHash, SourceGuidanceHashing.ToHashString(sourceSheet.SchemaHash), StringComparison.Ordinal))
            {
                throw new HspFormatException($"Compatible HSG sheet '{guidanceSheet.Name}' does not apply to the embedded source HXS.");
            }
        }

        using SourceGuidanceEvidenceHasher hasher = new(source.GameVersion, source.Scope, source.Language);
        foreach (GuidanceSheetMetadata sheet in sourceEvidence.Sheets.Values.OrderBy(sheet => sheet.Name, StringComparer.Ordinal))
        {
            hasher.AddSheet(sheet.Name, sheet.Variant, sheet.SchemaHash);
            foreach (GuidanceStringRow row in sourceEvidence.ReadStringRows(sheet.Name))
            {
                hasher.AddRow(row.RowId, row.SubrowId);
                foreach (GuidanceStringValue value in row.Values.OrderBy(value => value.ColumnIndex))
                {
                    hasher.AddStringOccurrence(value.ColumnIndex, value.MacroText);
                }
            }
        }

        string sourceEvidenceId = hasher.ComputeEvidenceId();
        SourceGuidanceEvidenceInput? sourceEvidenceInput = guidance.EvidenceInputs.SingleOrDefault(input => input.Language == source.Language);
        if (sourceEvidenceInput is null || !string.Equals(sourceEvidenceInput.EvidenceId, sourceEvidenceId, StringComparison.Ordinal))
        {
            throw new HspFormatException("Embedded source guidance evidence does not match the embedded source HXS.");
        }
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith("/", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Length >= 2 && path[1] == ':')
        {
            return false;
        }

        string[] segments = path.Split('/');
        return segments.All(segment => segment.Length != 0 && segment is not "." and not "..");
    }

    private static string MaterializedPath(string root, string archivePath)
    {
        string path = Path.GetFullPath(Path.Combine(root, archivePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new HspFormatException("HSP component path escapes its controlled materialization directory.");
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using MemoryStream output = new();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static bool SourceGuidanceLanguageIsCanonical(string language) =>
        language is "en" or "ja" or "de" or "fr" or "zh-cn" or "zh-tw" or "ko";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary validation state is owned by this process and best-effort cleanup is sufficient.
        }
    }
}
