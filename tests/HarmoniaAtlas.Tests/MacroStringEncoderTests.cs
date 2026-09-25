using System.Text.Json;
using HarmoniaAtlas.Cli;
using HarmoniaAtlas.Strings;
using Lumina.Text.ReadOnly;

namespace HarmoniaAtlas.Tests;

public sealed class MacroStringEncoderTests
{
    [Fact]
    public void EncodeCommandIsRecognized()
    {
        CliParseResult result = CommandLineParser.Parse(["encode", "--input", "in.jsonl", "--output", "out.jsonl"]);

        Assert.Null(result.Error);
        Assert.Equal(CliCommand.Encode, result.Command);
        Assert.Equal("in.jsonl", result.Options!.InputPath);
        Assert.Equal("out.jsonl", result.Options.OutputPath);
    }

    [Fact]
    public void EncodeCommandRequiresBothPaths()
    {
        Assert.NotNull(CommandLineParser.Parse(["encode", "--input", "in.jsonl"]).Error);
        Assert.NotNull(CommandLineParser.Parse(["encode", "--output", "out.jsonl", "--output", "x"]).Error);
    }

    [Theory]
    [InlineData("Привет, мир")]
    [InlineData("Hello <color(16711680)>red<color(stackcolor)><br>world")]
    public void CanonicalMacroTextRoundTrips(string macroText)
    {
        MacroEncodeResult result = MacroStringEncoder.Encode(macroText);

        Assert.NotNull(result.Bytes);
        Assert.Equal(macroText, new ReadOnlySeString(result.Bytes!).ToMacroString());
        Assert.DoesNotContain((byte)0, result.Bytes!);
    }

    [Fact]
    public void NonCanonicalNumbersEncodeLikeTheirCanonicalForm()
    {
        MacroEncodeResult hex = MacroStringEncoder.Encode("<color(0xFF0000)>red<color(stackcolor)>");
        MacroEncodeResult canonical = MacroStringEncoder.Encode("<color(16711680)>red<color(stackcolor)>");

        Assert.NotNull(hex.Bytes);
        Assert.Equal(canonical.Bytes, hex.Bytes);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("<color(", "invalidMacro")]
    public void InvalidMacroTextIsRejected(string macroText, string code)
    {
        MacroEncodeResult result = MacroStringEncoder.Encode(macroText);

        Assert.Null(result.Bytes);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public void FileEncodingKeepsOrderAndReportsFailuresPerLine()
    {
        string directory = Path.Combine(Path.GetTempPath(), "atlas encode тест " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "запросы.jsonl");
            string output = Path.Combine(directory, "результаты.jsonl");
            File.WriteAllText(input, "{\"macro\":\"Один\"}\n{\"macro\":\"<color(\"}\n{\"macro\":\"Три\"}\n");

            MacroEncodeSummary summary = MacroStringEncoder.EncodeFile(input, output);

            Assert.Equal(new MacroEncodeSummary(2, 1), summary);
            string[] lines = File.ReadAllLines(output);
            Assert.Equal(3, lines.Length);
            Assert.Equal(Convert.ToHexStringLower("Один"u8), JsonDocument.Parse(lines[0]).RootElement.GetProperty("hex").GetString());
            Assert.Equal("invalidMacro", JsonDocument.Parse(lines[1]).RootElement.GetProperty("error").GetString());
            Assert.Equal(Convert.ToHexStringLower("Три"u8), JsonDocument.Parse(lines[2]).RootElement.GetProperty("hex").GetString());
            Assert.False(File.Exists(output + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedRequestLineFailsTheCommand()
    {
        string directory = Path.Combine(Path.GetTempPath(), "atlas-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "in.jsonl");
            string output = Path.Combine(directory, "out.jsonl");
            File.WriteAllText(input, "{\"text\":\"x\"}\n");

            Assert.Throws<InvalidDataException>(() => MacroStringEncoder.EncodeFile(input, output));
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(output + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
