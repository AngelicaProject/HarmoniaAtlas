using HarmoniaAtlas.Cli;

namespace HarmoniaAtlas.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void ExtractCommandAndArgumentsAreRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(
        [
            "extract",
            "--game-path", "C:\\ffxiv",
            "--language", "English",
            "--output", "snapshot.hxs",
        ]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Extract, result.Command);
        Assert.Equal("C:\\ffxiv", result.Options!.GamePath);
        Assert.Equal("English", result.Options.Language);
        Assert.Equal("snapshot.hxs", result.Options.OutputPath);
    }

    [Fact]
    public void VerifyCommandIsRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(["verify", "snapshot.hxs"]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Verify, result.Command);
        Assert.Equal("snapshot.hxs", result.Options!.HxsPath);
    }

    [Fact]
    public void InspectJsonOptionIsRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(["inspect", "snapshot.hxs", "--json"]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Inspect, result.Command);
        Assert.True(result.Options!.Json);
    }
}
