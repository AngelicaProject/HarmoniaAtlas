using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HarmoniaAtlas.Guidance;
using HarmoniaAtlas.Hxs;
using HarmoniaAtlas.Model;
using HarmoniaAtlas.Package;

namespace HarmoniaAtlas.Tests;

public sealed class HspPackageTests
{
    [Fact]
    public void ValidPackageRoundTripsAndHasDeterministicIdentityAndBytes()
    {
        string root = NewDirectory();
        try
        {
            string source = CreateSnapshot(root, "en", "Fire Shard");
            string japanese = CreateSnapshot(root, "ja", "ファイアシャード");
            string german = CreateSnapshot(root, "de", "Feuerscherbe");
            string french = CreateSnapshot(root, "fr", "Éclat de feu");
            string guidance = Path.Combine(root, "guidance.json");
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], guidance);
            HspManifest manifest = BuildManifest(source, guidance);
            string first = Path.Combine(root, "first.hsp");
            string second = Path.Combine(root, "second.hsp");

            string firstPartial = new HspWriter().WritePartial(first, source, guidance, manifest);
            HspPackageSummary firstSummary = HspPackageValidator.Validate(firstPartial);
            HspWriter.Publish(firstPartial, first);
            string secondPartial = new HspWriter().WritePartial(second, source, guidance, manifest);
            HspPackageValidator.Validate(secondPartial);
            HspWriter.Publish(secondPartial, second);

            Assert.Equal(manifest.PackageId, firstSummary.Manifest.PackageId);
            Assert.Equal(2, firstSummary.Manifest.Components.Count(component => component.Required));
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            Assert.Equal(firstSummary.Manifest.PackageId, HspPackageValidator.Validate(second).Manifest.PackageId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ManifestRejectsDuplicateIdsPathsTraversalAndUnknownRequiredKinds()
    {
        HspManifest manifest = MinimalManifest();
        Assert.Throws<HspFormatException>(() => HspPackageValidator.ValidateManifest(manifest with
        {
            Components = [manifest.Components[0], manifest.Components[1] with { Id = manifest.Components[0].Id }],
        }));
        Assert.Throws<HspFormatException>(() => HspPackageValidator.ValidateManifest(manifest with
        {
            Components = [manifest.Components[0], manifest.Components[1] with { Path = "../source.hxs" }],
        }));
        Assert.Throws<HspFormatException>(() => HspPackageValidator.ValidateManifest(manifest with
        {
            Components = [manifest.Components[0], manifest.Components[1] with { Kind = "future", Required = true }],
        }));
    }

    [Fact]
    public void ValidatorRejectsComponentHashSizeAndUnlistedEntryTampering()
    {
        string root = NewDirectory();
        try
        {
            string source = CreateSnapshot(root, "en", "Fire Shard");
            string japanese = CreateSnapshot(root, "ja", "ファイアシャード");
            string german = CreateSnapshot(root, "de", "Feuerscherbe");
            string french = CreateSnapshot(root, "fr", "Éclat de feu");
            string guidance = Path.Combine(root, "guidance.json");
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], guidance);
            HspManifest manifest = BuildManifest(source, guidance);

            string hashMismatch = Path.Combine(root, "hash.hsp");
            WriteArchive(hashMismatch, manifest, source, guidance, tamperSource: true, extraEntry: false);
            Assert.Throws<HspFormatException>(() => HspPackageValidator.Validate(hashMismatch));

            string extra = Path.Combine(root, "extra.hsp");
            WriteArchive(extra, manifest, source, guidance, tamperSource: false, extraEntry: true);
            Assert.Throws<HspFormatException>(() => HspPackageValidator.Validate(extra));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void UnknownOptionalComponentIsIntegrityCheckedAndIgnored()
    {
        string root = NewDirectory();
        try
        {
            string source = CreateSnapshot(root, "en", "Fire Shard");
            string japanese = CreateSnapshot(root, "ja", "ファイアシャード");
            string german = CreateSnapshot(root, "de", "Feuerscherbe");
            string french = CreateSnapshot(root, "fr", "Éclat de feu");
            string guidance = Path.Combine(root, "guidance.json");
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], guidance);
            HspManifest baseManifest = BuildManifest(source, guidance);
            byte[] optional = Encoding.UTF8.GetBytes("optional");
            HspComponentDescriptor descriptor = new("optional", "future", 1, false, "optional/data.bin", optional.Length, HspHashing.ToHashString(System.Security.Cryptography.SHA256.HashData(optional)));
            HspManifest manifestWithoutId = baseManifest with { Components = baseManifest.Components.Append(descriptor).ToArray(), PackageId = string.Empty };
            HspManifest manifest = manifestWithoutId with { PackageId = HspHashing.ComputePackageId(manifestWithoutId) };
            string path = Path.Combine(root, "optional.hsp");
            WriteArchive(path, manifest, source, guidance, tamperSource: false, extraEntry: false, optional);

            HspPackageSummary summary = HspPackageValidator.Validate(path);
            Assert.Contains(summary.Manifest.Components, component => component.Kind == "future");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ValidatorRejectsSourceIdentityMismatch()
    {
        string root = NewDirectory();
        try
        {
            string source = CreateSnapshot(root, "en", "Fire Shard");
            string japanese = CreateSnapshot(root, "ja", "ファイアシャード");
            string german = CreateSnapshot(root, "de", "Feuerscherbe");
            string french = CreateSnapshot(root, "fr", "Éclat de feu");
            string guidance = Path.Combine(root, "guidance.json");
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], guidance);
            HspManifest baseManifest = BuildManifest(source, guidance);
            HspManifest wrong = baseManifest with
            {
                Source = baseManifest.Source with { ContentId = "sha256:" + new string('0', 64) },
                PackageId = string.Empty,
            };
            wrong = wrong with { PackageId = HspHashing.ComputePackageId(wrong) };
            string path = Path.Combine(root, "wrong.hsp");
            WriteArchive(path, wrong, source, guidance, tamperSource: false, extraEntry: false);
            Assert.Throws<HspFormatException>(() => HspPackageValidator.Validate(path));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PartialPublicationIsCleanedOnFailureAndStalePartialIsReplaced()
    {
        string root = NewDirectory();
        try
        {
            string source = CreateSnapshot(root, "en", "Fire Shard");
            string guidance = Path.Combine(root, "guidance.json");
            string missingGuidance = Path.Combine(root, "missing.json");
            string japanese = CreateSnapshot(root, "ja", "ファイアシャード");
            string german = CreateSnapshot(root, "de", "Feuerscherbe");
            string french = CreateSnapshot(root, "fr", "Éclat de feu");
            new SourceGuidanceGenerator().Generate(source, [japanese, german, french], guidance);
            HspManifest manifest = BuildManifest(source, guidance);
            string output = Path.Combine(root, "source.hsp");
            Assert.Throws<HspFormatException>(() => new HspWriter().WritePartial(output, source, missingGuidance, manifest));
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(output + ".partial"));

            File.WriteAllText(output + ".partial", "stale");
            string partial = new HspWriter().WritePartial(output, source, guidance, manifest);
            HspPackageValidator.Validate(partial);
            HspWriter.Publish(partial, output);
            Assert.True(File.Exists(output));
            Assert.False(File.Exists(output + ".partial"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static HspManifest BuildManifest(string source, string guidance)
    {
        HxsMetadata metadata = HxsVerifier.Verify(source).Metadata;
        HspComponentDescriptor sourceComponent = Component("source", "sourceHxs", "source/source.hxs", source);
        HspComponentDescriptor guidanceComponent = Component("guidance", "sourceGuidance", "guidance/source-guidance.json", guidance);
        HspManifest withoutId = new(
            1,
            string.Empty,
            metadata.GameVersion,
            metadata.Scope,
            new HspSourceIdentity(metadata.Language, metadata.ContentId, metadata.SnapshotId),
            [guidanceComponent, sourceComponent]);
        return withoutId with { PackageId = HspHashing.ComputePackageId(withoutId) };
    }

    private static HspManifest MinimalManifest()
    {
        HspComponentDescriptor source = new("source", "sourceHxs", 1, true, "source/source.hxs", 1, "sha256:" + new string('0', 64));
        HspComponentDescriptor guidance = new("guidance", "sourceGuidance", 1, true, "guidance/source-guidance.json", 1, "sha256:" + new string('1', 64));
        HspManifest withoutId = new(1, string.Empty, "game", "full", new HspSourceIdentity("en", "sha256:" + new string('2', 64), "sha256:" + new string('3', 64)), [guidance, source]);
        return withoutId with { PackageId = HspHashing.ComputePackageId(withoutId) };
    }

    private static void WriteArchive(
        string path,
        HspManifest manifest,
        string source,
        string guidance,
        bool tamperSource,
        bool extraEntry,
        byte[]? optional = null)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        WriteBytes(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest) is byte[] manifestBytes ? manifestBytes.Append((byte)'\n').ToArray() : throw new InvalidOperationException());
        WriteFile(archive, "guidance/source-guidance.json", guidance);
        WriteFile(archive, "source/source.hxs", tamperSource ? guidance : source);
        if (optional is not null)
        {
            WriteBytes(archive, "optional/data.bin", optional);
        }

        if (extraEntry)
        {
            WriteBytes(archive, "debug.txt", Encoding.UTF8.GetBytes("unexpected"));
        }
    }

    private static void WriteFile(ZipArchive archive, string entryName, string path)
    {
        WriteBytes(archive, entryName, File.ReadAllBytes(path));
    }

    private static void WriteBytes(ZipArchive archive, string entryName, byte[] bytes)
    {
        using Stream output = archive.CreateEntry(entryName).Open();
        output.Write(bytes);
    }

    private static HspComponentDescriptor Component(string id, string kind, string path, string filePath) =>
        new(id, kind, 1, true, path, new FileInfo(filePath).Length, HspHashing.ComputeFileHash(filePath));

    private static string CreateSnapshot(string root, string language, string value)
    {
        string path = Path.Combine(root, language + ".hxs");
        const string sheetName = "Item";
        HarmoniaColumnDefinition[] columns = [new(0, 0, HarmoniaColumnType.String)];
        HxsStringCellRecord cell = new(1, 0, 0, value, null, HxsHashing.HashMacro(value), null);
        byte[] technicalHash = HxsHashing.HashRowTechnical(sheetName, 1, 0, []);
        byte[] stringHash = HxsHashing.HashRowStrings(sheetName, 1, 0, [cell]);
        HxsRowRecord row = new(1, 0, [], HxsHashing.HashRow(sheetName, 1, 0, technicalHash, stringHash), technicalHash, stringHash, [cell]);
        HxsSheetHashAccumulator accumulator = new(sheetName);
        accumulator.AddRow(row);
        byte[] schemaHash = HxsHashing.HashSchema(sheetName, HarmoniaSheetVariant.DefaultRows, columns);
        byte[] sheetTechnicalHash = accumulator.ComputeTechnicalHash();
        byte[] sheetStringHash = accumulator.ComputeStringHash();
        HxsSheetRecord sheet = new(sheetName, HarmoniaSheetVariant.DefaultRows, "none", columns, 1, schemaHash, sheetTechnicalHash, sheetStringHash, HxsHashing.HashSheetContent(sheetName, HarmoniaSheetVariant.DefaultRows, schemaHash, sheetTechnicalHash, sheetStringHash));
        string contentId = HxsHashing.ComputeContentId(language, [sheet]);
        using HxsWriteSession session = new HxsWriter().Begin(path);
        int sheetId = session.BeginSheet(sheet);
        session.WriteRow(sheetId, row);
        session.CompleteSheet(sheetId, sheet);
        session.WriteMetadata(new HxsMetadata(1, "game", language, "full", contentId, HxsHashing.ComputeSnapshotId("game", language, contentId), "test", "7.7.0", 1, 1, 1));
        session.Complete();
        return path;
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "harmonia-atlas-hsp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
