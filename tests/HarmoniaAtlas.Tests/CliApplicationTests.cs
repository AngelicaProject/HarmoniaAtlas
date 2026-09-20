using System.Text;
using System.Text.Json;
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

    [Fact]
    public void PackageJsonlFailureIsAJsonLinesStreamWithOneTerminalFailure()
    {
        StringBuilder outputText = new();
        StringBuilder diagnosticsText = new();
        using StringWriter output = new(outputText);
        using StringWriter diagnostics = new(diagnosticsText);
        CliParseResult parse = CommandLineParser.Parse(
        [
            "package",
            "--game-path", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            "--language", "en",
            "--output", Path.Combine(Path.GetTempPath(), "hsp-test.hsp"),
            "--events", "jsonl",
        ]);

        int exitCode = CliApplication.Execute(parse, output, diagnostics);

        Assert.Equal(1, exitCode);
        string[] lines = outputText.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        JsonElement[] events = lines.Select(line => JsonDocument.Parse(line)).Select(document => document.RootElement.Clone()).ToArray();
        Assert.Equal("started", events[0].GetProperty("type").GetString());
        Assert.Equal("failed", events[^1].GetProperty("type").GetString());
        Assert.DoesNotContain(events, value => value.GetProperty("type").GetString() == "completed");
        Assert.Contains("error:", diagnosticsText.ToString(), StringComparison.Ordinal);
    }
}
