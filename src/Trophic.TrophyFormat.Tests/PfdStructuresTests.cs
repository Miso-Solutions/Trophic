using Trophic.TrophyFormat.Crypto;

namespace Trophic.TrophyFormat.Tests;

public class PfdStructuresTests
{
    [Fact]
    public void PfdConstants_FileSizeIs32768()
    {
        Assert.Equal(32768, PfdConstants.FileSize);
    }

    [Fact]
    public void PfdConstants_MagicContainsPFDB()
    {
        // The magic value 0x0000000050464442 is "PFDB" in ASCII (big-endian, zero-padded)
        var bytes = BitConverter.GetBytes(PfdConstants.Magic);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        // Last 4 bytes should spell "PFDB"
        Assert.Equal((byte)'P', bytes[4]);
        Assert.Equal((byte)'F', bytes[5]);
        Assert.Equal((byte)'D', bytes[6]);
        Assert.Equal((byte)'B', bytes[7]);
    }

    [Fact]
    public void PfdConstants_EntrySize272Bytes()
    {
        Assert.Equal(272, PfdConstants.EntrySize);
    }

    [Fact]
    public void PfdConstants_HashSize20Bytes()
    {
        Assert.Equal(20, PfdConstants.HashSize);
    }

    [Fact]
    public void PfdConstants_SectionOffsetsAreContiguous()
    {
        // Header(16) + HeaderKey(16) + Signature(64) = HashTableOffset(96)
        Assert.Equal(
            PfdConstants.HeaderSize + PfdConstants.HeaderKeySize + PfdConstants.SignatureSize,
            PfdConstants.HashTableOffset);
    }

    [Fact]
    public void PfdFile_ConstructorRejectsTooSmall()
    {
        var tooSmall = new byte[100];
        Assert.ThrowsAny<Exception>(() => new PfdFile(tooSmall));
    }

    [Fact]
    public void PfdFile_ConstructorRejectsWrongMagic()
    {
        var data = new byte[PfdConstants.FileSize];
        // Wrong magic — should fail
        Assert.ThrowsAny<Exception>(() => new PfdFile(data));
    }
}
