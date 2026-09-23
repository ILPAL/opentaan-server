using OpenTaan.Transport;

namespace OpenTaan.Tests.Transport;

/// <summary>
/// The cipher, checked against values worked out by hand. Every seed here is
/// invented: none of it comes from a recorded session.
/// </summary>
public sealed class OpenTaanCipherTests
{
    private const ulong Seed1 = 0x0123456789ABCDEF;
    private const ulong Seed2 = 0x0F1E2D3C4B5A6978;

    [Theory]
    [InlineData(0, 0xA628DE78u)]
    [InlineData(3, 0xD9FEF4FDu)]
    [InlineData(5, 0x6FB8BD5Bu)]
    public void DerivesTheSeedTheHandshakeImplies(byte shift, uint expected)
    {
        Assert.Equal(expected, OpenTaanCipher.DeriveSeed(Seed1, Seed2, shift));
    }

    [Fact]
    public void ShiftUsesOnlyItsLowSixBits()
    {
        // The client shifts 64-bit registers, so the count is masked rather than
        // shifting the value away entirely. 69 is 5 once masked.
        Assert.Equal(
            OpenTaanCipher.DeriveSeed(Seed1, Seed2, 5),
            OpenTaanCipher.DeriveSeed(Seed1, Seed2, 69));

        Assert.NotEqual(
            OpenTaanCipher.DeriveSeed(Seed1, Seed2, 0),
            OpenTaanCipher.DeriveSeed(Seed1, Seed2, 5));
    }

    [Fact]
    public void AdvancingStepsByTheIncrement()
    {
        Assert.Equal(0x11223344u + OpenTaanCipher.SeedIncrement, OpenTaanCipher.AdvanceSeed(0x11223344u));
    }

    [Fact]
    public void AdvancingNeverLandsOnTheSeedReservedForPlaintext()
    {
        // This base is one increment short of wrapping onto zero.
        const uint LandsOnZero = 0xFEB8C0E7;

        Assert.Equal(OpenTaanCipher.PlaintextSeed, unchecked(LandsOnZero + OpenTaanCipher.SeedIncrement));
        Assert.Equal(1u, OpenTaanCipher.AdvanceSeed(LandsOnZero));
    }

    [Fact]
    public void DerivesTheKeyWordsFromTheSeed()
    {
        Span<byte> key = stackalloc byte[OpenTaanCipher.KeyLength];
        OpenTaanCipher.DeriveKey(0x11223344, key);

        Assert.Equal(
            new byte[]
            {
                0x8C, 0x34, 0x91, 0x05,
                0xE8, 0x21, 0x9D, 0x9D,
                0x85, 0xA4, 0x21, 0x35,
                0x84, 0x1A, 0x9F, 0xE2,
            },
            key.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void RoundTripsABodyOfAnyLength(int length)
    {
        var plaintext = Body(length);
        var ciphertext = new byte[length];
        var recovered = new byte[length];

        OpenTaanCipher.Encrypt(plaintext, ciphertext, 0x3092A13F);
        OpenTaanCipher.Decrypt(ciphertext, recovered, 0x3092A13F);

        Assert.Equal(plaintext, recovered);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(48)]
    public void BothDirectionsChecksumTheSameBytes(int length)
    {
        // The transformation is its own inverse, so checksumming whatever was fed in
        // would make encryption checksum plaintext and decryption checksum ciphertext.
        // They coincide only for an even number of whole key blocks, so a body of one
        // block, or any partial block, catches the mistake.
        var plaintext = Body(length);
        var ciphertext = new byte[length];
        var recovered = new byte[length];

        var written = OpenTaanCipher.Encrypt(plaintext, ciphertext, 0x3092A13F);
        var read = OpenTaanCipher.Decrypt(ciphertext, recovered, 0x3092A13F);

        Assert.Equal(written, read);
    }

    [Fact]
    public void EncryptingActuallyChangesTheBytes()
    {
        var plaintext = Body(32);
        var ciphertext = new byte[plaintext.Length];

        OpenTaanCipher.Encrypt(plaintext, ciphertext, 0x3092A13F);

        Assert.NotEqual(plaintext, ciphertext);
    }

    private static byte[] Body(int length)
    {
        var body = new byte[length];
        for (var i = 0; i < length; i++)
        {
            body[i] = (byte)((i * 7) + 3);
        }

        return body;
    }
}
