using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HarmoniaAtlas.Hxs;

public sealed class CanonicalHasher
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public ReadOnlyMemory<byte> CanonicalBytes => _buffer.WrittenMemory;

    public void WriteByte(byte value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(byte));
        destination[0] = value;
        _buffer.Advance(sizeof(byte));
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        _buffer.Advance(sizeof(ushort));
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        _buffer.Advance(sizeof(uint));
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        _buffer.Advance(sizeof(ulong));
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

    public void WriteSingle(float value) => WriteUInt32(BitConverter.SingleToUInt32Bits(value));

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> destination = _buffer.GetSpan(bytes.Length);
        bytes.CopyTo(destination);
        _buffer.Advance(bytes.Length);
    }

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> bytes)
    {
        if ((uint)bytes.Length != bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Byte input is too large for canonical length encoding.");
        }

        WriteUInt32((uint)bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if ((uint)byteCount != byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "UTF-8 input is too large for canonical length encoding.");
        }

        WriteUInt32((uint)byteCount);
        Span<byte> destination = _buffer.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), destination);
        _buffer.Advance(written);
    }

    public void WriteDomain(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        WriteBytes(Encoding.UTF8.GetBytes(domain));
    }

    public byte[] ComputeHash() => SHA256.HashData(_buffer.WrittenSpan);

    public static byte[] ComputeHash(Action<CanonicalHasher> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        CanonicalHasher hasher = new();
        write(hasher);
        return hasher.ComputeHash();
    }
}
