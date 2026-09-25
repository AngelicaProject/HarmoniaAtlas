using System.Text.Json;
using Lumina.Text.Parse;
using Lumina.Text.ReadOnly;

namespace HarmoniaAtlas.Strings;

public sealed record MacroEncodeResult(byte[]? Bytes, string? ErrorCode, string? Message)
{
    public static MacroEncodeResult Success(byte[] bytes) => new(bytes, null, null);

    public static MacroEncodeResult Failure(string code, string message) => new(null, code, message);
}

public sealed record MacroEncodeSummary(int Encoded, int Failed);

// Encodes macro text (the HXS macro_text syntax) into the SeString bytes the
// game reads, using the same Lumina version that extraction uses to print it.
public static class MacroStringEncoder
{
    public const int MaxEncodedLength = 65535;

    private static readonly MacroStringParseOptions ParseOptions = new(default);

    public static MacroEncodeResult Encode(string macroText)
    {
        ArgumentNullException.ThrowIfNull(macroText);
        if (macroText.Length == 0)
        {
            return MacroEncodeResult.Failure("empty", "macro text is empty");
        }

        byte[] bytes;
        try
        {
            bytes = ReadOnlySeString.FromMacroString(macroText, ParseOptions).Data.ToArray();
        }
        catch (MacroStringParseException exception)
        {
            return MacroEncodeResult.Failure("invalidMacro", exception.Message);
        }

        if (bytes.Length == 0)
        {
            return MacroEncodeResult.Failure("empty", "macro text encodes to no bytes");
        }

        if (bytes.Contains((byte)0))
        {
            return MacroEncodeResult.Failure("containsNul", "encoded string contains a NUL byte");
        }

        if (bytes.Length > MaxEncodedLength)
        {
            return MacroEncodeResult.Failure("tooLong", $"encoded string is {bytes.Length} bytes, the limit is {MaxEncodedLength}");
        }

        // Lumina may print macro text in a canonical form (for example hex
        // numbers as decimal), so the check is on bytes: decoding and encoding
        // again must reproduce exactly the same string.
        string decoded = new ReadOnlySeString(bytes).ToMacroString();
        byte[] again = ReadOnlySeString.FromMacroString(decoded, ParseOptions).Data.ToArray();
        if (!again.AsSpan().SequenceEqual(bytes))
        {
            return MacroEncodeResult.Failure("notRoundTrip", "encoded bytes do not survive a decode and encode round trip");
        }

        return MacroEncodeResult.Success(bytes);
    }

    // Reads {"macro": "..."} lines and writes one result line per request, in
    // order. The output file appears only after every line was written.
    public static MacroEncodeSummary EncodeFile(string inputPath, string outputPath)
    {
        string temporaryPath = outputPath + ".tmp";
        int encoded = 0;
        int failed = 0;
        try
        {
            using (StreamReader reader = new(inputPath, new System.Text.UTF8Encoding(false, true)))
            using (StreamWriter writer = new(temporaryPath, false, new System.Text.UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                int lineNumber = 0;
                while (reader.ReadLine() is { } line)
                {
                    lineNumber++;
                    string macroText = ReadRequest(line, lineNumber);
                    MacroEncodeResult result = Encode(macroText);
                    writer.WriteLine(WriteResult(result));
                    if (result.Bytes is null)
                    {
                        failed++;
                    }
                    else
                    {
                        encoded++;
                    }
                }
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            return new MacroEncodeSummary(encoded, failed);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static string ReadRequest(string line, int lineNumber)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("macro", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                document.RootElement.EnumerateObject().Count() == 1)
            {
                return value.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidDataException($"encode input line {lineNumber} is not a {{\"macro\": string}} object");
    }

    private static string WriteResult(MacroEncodeResult result)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            if (result.Bytes is not null)
            {
                json.WriteString("hex", Convert.ToHexStringLower(result.Bytes));
            }
            else
            {
                json.WriteString("error", result.ErrorCode);
                json.WriteString("message", result.Message);
            }

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
