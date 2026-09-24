using System.Reflection;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Tests;

public sealed class AtlasApplicationVersionTests
{
    [Fact]
    public void CurrentVersionComesFromAssemblyInformationalVersion()
    {
        string informationalVersion = typeof(AtlasApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Equal(
            AtlasApplicationVersion.NormalizeInformationalVersion(informationalVersion),
            AtlasApplicationVersion.Current);
        Assert.Equal("0.3.0", AtlasApplicationVersion.Current);
    }

    [Theory]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("0.3.0+abcdef", "0.3.0")]
    [InlineData(" 0.3.0+abcdef ", "0.3.0")]
    public void InformationalVersionMetadataIsRemoved(string informationalVersion, string expected)
    {
        Assert.Equal(expected, AtlasApplicationVersion.NormalizeInformationalVersion(informationalVersion));
    }
}
