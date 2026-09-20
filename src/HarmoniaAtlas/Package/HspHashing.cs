using System.Security.Cryptography;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Package;

public static class HspHashing
{
    public const string PackageHashDomain = "HARMONIA-SOURCE-PACKAGE-v1";

    public static string ComputePackageId(HspManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        byte[] hash = CanonicalHasher.ComputeHash(hasher =>
        {
            hasher.WriteDomain(PackageHashDomain);
            hasher.WriteInt32(manifest.FormatVersion);
            hasher.WriteUtf8(manifest.GameVersion);
            hasher.WriteUtf8(manifest.Scope);
            hasher.WriteUtf8(manifest.Source.Language);
            hasher.WriteUtf8(manifest.Source.ContentId);
            hasher.WriteUtf8(manifest.Source.SnapshotId);

            HspComponentDescriptor[] components = manifest.Components
                .OrderBy(component => component.Id, StringComparer.Ordinal)
                .ToArray();
            hasher.WriteUInt32(checked((uint)components.Length));
            foreach (HspComponentDescriptor component in components)
            {
                hasher.WriteUtf8(component.Id);
                hasher.WriteUtf8(component.Kind);
                hasher.WriteInt32(component.FormatVersion);
                hasher.WriteByte(component.Required ? (byte)1 : (byte)0);
                hasher.WriteUtf8(component.Path);
                hasher.WriteInt64(component.Size);
                hasher.WriteUtf8(component.Sha256);
            }
        });

        return ToHashString(hash);
    }

    public static string ComputeFileHash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ToHashString(SHA256.HashData(stream));
    }

    public static string ToHashString(ReadOnlySpan<byte> hash) =>
        "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();

    public static bool IsSha256(string? value) =>
        value is not null && value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
