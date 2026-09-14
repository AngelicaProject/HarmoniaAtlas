using System.Reflection;

namespace HarmoniaAtlas.Hxs;

public static class AtlasApplicationVersion
{
    public static string Current { get; } = ReadApplicationVersion();

    public static string NormalizeInformationalVersion(string informationalVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(informationalVersion);

        int metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return (metadataSeparator < 0 ? informationalVersion : informationalVersion[..metadataSeparator]).Trim();
    }

    private static string ReadApplicationVersion()
    {
        string informationalVersion = typeof(AtlasApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidOperationException("The application informational version is not available.");
        return NormalizeInformationalVersion(informationalVersion);
    }
}
