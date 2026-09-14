using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Tests;

public sealed class CanonicalHasherTests
{
    [Fact]
    public void IntegerEncodingIsLittleEndianAndDeterministic()
    {
        CanonicalHasher hasher = new();
        hasher.WriteByte(0xAB);
        hasher.WriteUInt16(0x1234);
        hasher.WriteUInt32(0x12345678);
        hasher.WriteUInt64(0x0123456789ABCDEF);

        Assert.Equal(
            new byte[]
            {
                0xAB,
                0x34, 0x12,
                0x78, 0x56, 0x34, 0x12,
                0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
            },
            hasher.CanonicalBytes.ToArray());
    }

    [Fact]
    public void Utf8EncodingIsLengthPrefixed()
    {
        CanonicalHasher hasher = new();
        hasher.WriteUtf8("é");

        Assert.Equal(new byte[] { 2, 0, 0, 0, 0xC3, 0xA9 }, hasher.CanonicalBytes.ToArray());
    }

    [Fact]
    public void SameCanonicalInputProducesSameSha256()
    {
        static byte[] HashInput()
        {
            CanonicalHasher hasher = new();
            hasher.WriteUInt32(42);
            hasher.WriteUtf8("HarmoniaAtlas");
            hasher.WriteBytes(new byte[] { 1, 2, 3 });
            return hasher.ComputeHash();
        }

        Assert.Equal(HashInput(), HashInput());
    }
}
