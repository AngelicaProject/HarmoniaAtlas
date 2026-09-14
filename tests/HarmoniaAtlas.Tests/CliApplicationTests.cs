using System.Text;
using HarmoniaAtlas.Cli;
using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void VersionCommandWritesOnlyTheLogicalApplicationVersion()
    {
        StringBuilder output = new();
        using StringWriter writer = new(output);

        int exitCode = CliApplication.Execute(CommandLineParser.Parse(["--version"]), writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(AtlasApplicationVersion.Current + Environment.NewLine, output.ToString());
    }
}
