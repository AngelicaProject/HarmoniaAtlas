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
    public void GuidanceCommandAcceptsExplicitSourceAndComparisonsAndJson()
    {
        CliParseResult result = CommandLineParser.Parse(
        [
            "guidance",
            "--source", "source-en.hxs",
            "--compare", "source-ja.hxs",
            "--compare", "source-de.hxs",
            "--output", "source.hsg.json",
            "--json",
        ]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Guidance, result.Command);
        Assert.Equal("source-en.hxs", result.Options!.SourcePath);
        Assert.Equal(["source-ja.hxs", "source-de.hxs"], result.Options.ComparePaths);
        Assert.Equal("source.hsg.json", result.Options.OutputPath);
        Assert.True(result.Options.Json);
    }

    [Fact]
    public void GuidanceCommandRejectsDuplicateSourceOrComparisonPath()
    {
        CliParseResult result = CommandLineParser.Parse(
        [
            "guidance",
            "--source", "source-en.hxs",
            "--compare", "source-en.hxs",
            "--output", "source.hsg.json",
        ]);

        Assert.NotNull(result.Error);
        Assert.Contains("duplicated", result.Error, StringComparison.Ordinal);
    }
}
