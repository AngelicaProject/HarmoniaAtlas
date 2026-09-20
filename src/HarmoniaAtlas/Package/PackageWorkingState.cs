using System.Security.Cryptography;
using System.Text;

namespace HarmoniaAtlas.Package;

public static class PackageWorkingState
{
    public static string GetRoot(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(fullOutputPath));
        string identity = Convert.ToHexString(digest).ToLowerInvariant();
        return Path.Combine(GetBaseRoot(), identity);
    }

    public static string Prepare(string outputPath)
    {
        string root = GetRoot(outputPath);
        EnsureOwnedRoot(root);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        return root;
    }

    public static void Cleanup(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        EnsureOwnedRoot(fullRoot);
        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private static string GetBaseRoot() => Path.Combine(Path.GetTempPath(), "harmonia-atlas", "package");

    private static void EnsureOwnedRoot(string root)
    {
        string baseRoot = Path.GetFullPath(GetBaseRoot()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(root);
        string? parent = Path.GetDirectoryName(fullRoot);
        string name = Path.GetFileName(fullRoot);
        if (parent is null || !string.Equals(
                parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                baseRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            name.Length != 64 || name.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The supplied package working root is not an Atlas-owned deterministic root.", nameof(root));
        }
    }
}
