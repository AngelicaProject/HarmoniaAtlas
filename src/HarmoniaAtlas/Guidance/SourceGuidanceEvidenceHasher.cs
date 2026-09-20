using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HarmoniaAtlas.Model;

namespace HarmoniaAtlas.Guidance;

/// <summary>
/// Streams the canonical physical String corpus used to derive one guidance evidence fingerprint.
/// </summary>
public sealed class SourceGuidanceEvidenceHasher : IDisposable
{
    public const string HashDomain = "HARMONIA-SOURCE-GUIDANCE-EVIDENCE-v1";

    private const byte SheetStart = 0x01;
    private const byte RowStart = 0x02;
    private const byte StringOccurrence = 0x03;
    private const byte RowEnd = 0x04;
    private const byte SheetEnd = 0x05;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _previousSheet;
    private string? _currentSheet;
    private bool _hasCurrentRow;
    private uint _previousRowId;
    private ushort _previousSubrowId;
    private int _previousColumnIndex;
    private bool _completed;
    private string? _evidenceId;

    public SourceGuidanceEvidenceHasher(string gameVersion, string scope, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        WriteDomain(HashDomain);
        WriteUtf8(gameVersion);
        WriteUtf8(scope);
        WriteUtf8(language);
    }

    public void AddSheet(string sheetName, HarmoniaSheetVariant variant, ReadOnlySpan<byte> schemaHash)
    {
        EnsureWritable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        if (!Enum.IsDefined(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unsupported sheet variant.");
        }

        if (schemaHash.Length != 32)
        {
            throw new ArgumentException("Schema hash must be a SHA-256 value.", nameof(schemaHash));
        }

        if (_previousSheet is not null && string.CompareOrdinal(_previousSheet, sheetName) >= 0)
        {
            throw new ArgumentException("Evidence sheets must be added in ordinal name order.", nameof(sheetName));
        }

        FinishSheet();
        WriteByte(SheetStart);
        WriteUtf8(sheetName);
        WriteUInt32((uint)variant);
        WriteLengthPrefixedBytes(schemaHash);
        _previousSheet = sheetName;
        _currentSheet = sheetName;
        _hasCurrentRow = false;
    }

    public void AddRow(uint rowId, ushort subrowId)
    {
        EnsureWritable();
        if (_currentSheet is null)
        {
            throw new InvalidOperationException("An evidence sheet must be added before its rows.");
        }

        if (_hasCurrentRow && (rowId < _previousRowId ||
            rowId == _previousRowId && subrowId <= _previousSubrowId))
        {
            throw new ArgumentException("Evidence rows must be added in row/subrow order.", nameof(rowId));
        }

        if (_hasCurrentRow)
        {
            WriteByte(RowEnd);
        }

        WriteByte(RowStart);
        WriteUInt32(rowId);
        WriteUInt32(subrowId);
        _hasCurrentRow = true;
        _previousRowId = rowId;
        _previousSubrowId = subrowId;
        _previousColumnIndex = -1;
    }

    public void AddStringOccurrence(int columnIndex, string macroText)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(macroText);
        if (!_hasCurrentRow)
        {
            throw new InvalidOperationException("An evidence row must be added before its String occurrences.");
        }

        if (columnIndex < 0 || columnIndex <= _previousColumnIndex)
        {
            throw new ArgumentException("Evidence String occurrences must be added in column order.", nameof(columnIndex));
        }

        WriteByte(StringOccurrence);
        WriteUInt32(checked((uint)columnIndex));
        WriteUtf8(macroText);
        _previousColumnIndex = columnIndex;
    }

    public string ComputeEvidenceId()
    {
        if (_evidenceId is not null)
        {
            return _evidenceId;
        }

        FinishSheet();
        _evidenceId = SourceGuidanceHashing.ToHashString(_hash.GetHashAndReset());
        _completed = true;
        return _evidenceId;
    }

    public void Dispose() => _hash.Dispose();

    private void EnsureWritable()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The evidence hash has already been computed.");
        }
    }

    private void FinishSheet()
    {
        FinishRow();
        if (_currentSheet is not null)
        {
            WriteByte(SheetEnd);
            _currentSheet = null;
        }
    }

    private void FinishRow()
    {
        if (_hasCurrentRow)
        {
            WriteByte(RowEnd);
            _hasCurrentRow = false;
        }
    }

    private void WriteByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        bytes[0] = value;
        _hash.AppendData(bytes);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    private void WriteLengthPrefixedBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        _hash.AppendData(value);
    }

    private void WriteUtf8(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(checked((uint)bytes.Length));
        _hash.AppendData(bytes);
    }

    private void WriteDomain(string value) => _hash.AppendData(Encoding.UTF8.GetBytes(value));
}
