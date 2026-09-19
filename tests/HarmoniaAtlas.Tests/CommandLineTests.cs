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

    [Fact]
    public void VersionOptionIsRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(["--version"]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Version, result.Command);
    }

    [Fact]
    public void VersionCommandIsRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(["version"]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Version, result.Command);
    }

    [Fact]
    public void GuidanceCommandAcceptsRepeatableInputsAndJson()
    {
        CliParseResult result = CommandLineParser.Parse(
        [
            "guidance",
            "--input", "source-en.hxs",
            "--input", "source-ja.hxs",
            "--output", "source.hsg.json",
            "--json",
        ]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Guidance, result.Command);
        Assert.Equal(["source-en.hxs", "source-ja.hxs"], result.Options!.InputPaths);
        Assert.Equal("source.hsg.json", result.Options.OutputPath);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void GuidanceCommandRejectsDuplicateInputPath()
    {
        CliParseResult result = CommandLineParser.Parse(
        [
            "guidance",
            "--input", "source-en.hxs",
            "--input", "source-en.hxs",
            "--output", "source.hsg.json",
        ]);

        Assert.NotNull(result.Error);
        Assert.Contains("duplicated", result.Error, StringComparison.Ordinal);
    }
}
