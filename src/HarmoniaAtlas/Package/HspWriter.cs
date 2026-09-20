using System.IO.Compression;
using System.Text;

namespace HarmoniaAtlas.Package;

public sealed class HspWriter
{
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string WritePartial(
        string outputPath,
        string sourcePath,
        string guidancePath,
        HspManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(guidancePath);
        ArgumentNullException.ThrowIfNull(manifest);
        HspPackageValidator.ValidateManifest(manifest);

        string fullOutputPath = Path.GetFullPath(outputPath);
        string partialPath = fullOutputPath + ".partial";
        TryDelete(partialPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        Dictionary<string, string> files = new(StringComparer.Ordinal)
        {
            ["source/source.hxs"] = Path.GetFullPath(sourcePath),
            ["guidance/source-guidance.json"] = Path.GetFullPath(guidancePath),
        };
        foreach (HspComponentDescriptor component in manifest.Components)
        {
            if (!files.TryGetValue(component.Path, out string? filePath) || !File.Exists(filePath))
            {
                throw new HspFormatException($"No package input file is available for component '{component.Id}'.");
            }
        }

        byte[] manifestBytes = HspJson.SerializeManifest(manifest);
        try
        {
            using (FileStream stream = new(partialPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
            {
                WriteEntry(archive, "manifest.json", manifestBytes);
                foreach (HspComponentDescriptor component in manifest.Components.OrderBy(component => component.Path, StringComparer.Ordinal))
                {
                    WriteFileEntry(archive, component.Path, files[component.Path]);
                }
            }

            using FileStream flush = new(partialPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            flush.Flush(flushToDisk: true);
            return partialPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public static void Publish(string partialPath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partialPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullPartialPath = Path.GetFullPath(partialPath);
        string fullOutputPath = Path.GetFullPath(outputPath);
        if (!File.Exists(fullPartialPath))
        {
            throw new FileNotFoundException("HSP partial archive was not found.", fullPartialPath);
        }

        File.Move(fullPartialPath, fullOutputPath, overwrite: true);
    }

    private static void WriteFileEntry(ZipArchive archive, string entryName, string filePath)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        entry.LastWriteTime = FixedTimestamp;
        using FileStream input = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using Stream output = entry.Open();
        input.CopyTo(output);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        entry.LastWriteTime = FixedTimestamp;
        using Stream output = entry.Open();
        output.Write(bytes);
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
            // Preserve the original failure. A stale partial is never a valid package.
        }
    }
}
