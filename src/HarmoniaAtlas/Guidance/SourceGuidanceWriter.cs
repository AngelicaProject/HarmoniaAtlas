using System.Text;
using System.Text.Json;

namespace HarmoniaAtlas.Guidance;

public static class SourceGuidanceWriter
{
    public static void Write(SourceGuidanceBundle bundle, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        SourceGuidanceReader.Validate(bundle);

        string fullTargetPath = Path.GetFullPath(targetPath);
        string partialPath = fullTargetPath + ".partial";
        if (File.Exists(fullTargetPath))
        {
            throw new IOException($"Source guidance target already exists: {fullTargetPath}");
        }

        if (File.Exists(partialPath))
        {
            throw new IOException($"Source guidance partial target already exists: {partialPath}");
        }

        string? directory = Path.GetDirectoryName(fullTargetPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            string json = JsonSerializer.Serialize(bundle, SourceGuidanceJson.Options) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (FileStream stream = new(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            _ = SourceGuidanceReader.Read(partialPath);
            File.Move(partialPath, fullTargetPath);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
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
            // Preserve the generation failure. An orphaned partial remains visible for cleanup.
        }
    }
}
