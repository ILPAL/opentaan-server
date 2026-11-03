using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Data;

namespace OpenTaan.Tests.Protocol;

/// <summary>The client's data envelope: plain, compressed, scrambled, and hostile.</summary>
public sealed class DataEnvelopeTests
{
    private const int Limit = 1 << 16;

    // Repetitive, so it compresses well, and long enough to cross the 64-byte keystream.
    private static readonly byte[] Contents = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("channel list entry; ", 20)));

    [Fact]
    public void APlainEnvelopeIsTheSignatureNoFlagsTheChecksumAndTheContents()
    {
        byte[] contents = [0x01, 0x02, 0x03];

        byte[] expected = [(byte)'S', 0x00, .. Encoded.UInt32(ZeroStartAdler32.Of(contents)), .. contents];

        Assert.Equal(expected, DataEnvelope.Wrap(contents));
    }

    [Fact]
    public void TheChecksumStartsItsRunningSumAtZero()
    {
        // Standard Adler-32 of "abc" is 0x024D0127; starting the sum at 0 gives 0x024A0126.
        Assert.Equal(0x024A0126u, ZeroStartAdler32.Of("abc"u8));
    }

    [Theory]
    [InlineData(false, 0u)]
    [InlineData(true, 0u)]
    [InlineData(false, 0x1234ABCDu)]
    [InlineData(true, 0x0BADF00Du)]
    public void EveryCombinationUnwrapsToItsContents(bool compress, uint scrambleKey)
    {
        var envelope = DataEnvelope.Wrap(Contents, compress, scrambleKey);

        Assert.Equal(PacketError.None, DataEnvelope.TryUnwrap(envelope, Limit, out var contents));
        Assert.Equal(Contents, contents);
    }

    [Fact]
    public void ACompressedEnvelopeCarriesItsFlagAndItsSizeAfterTheChecksum()
    {
        var envelope = DataEnvelope.Wrap(Contents, compress: true);

        Assert.Equal(0x01, envelope[1]);
        Assert.Equal((uint)Contents.Length, BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(6)));
        Assert.True(envelope.Length < Contents.Length);
    }

    [Fact]
    public void AScrambledCompressedEnvelopeCarriesTheKeyBeforeTheSize()
    {
        var envelope = DataEnvelope.Wrap(Contents, compress: true, scrambleKey: 0x0BADF00D);

        Assert.Equal(0x03, envelope[1]);
        Assert.Equal(0x0BADF00Du, BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(6)));
        Assert.Equal((uint)Contents.Length, BinaryPrimitives.ReadUInt32LittleEndian(envelope.AsSpan(10)));
    }

    [Fact]
    public void ContentsThatDoNotShrinkAreLeftUncompressed()
    {
        // Compressing three bytes costs more than it saves, so the client would not.
        byte[] contents = [0x01, 0x02, 0x03];

        Assert.Equal(0x00, DataEnvelope.Wrap(contents, compress: true)[1]);
    }

    [Fact]
    public void ScramblingChangesThePayloadButNotTheChecksum()
    {
        var plain = DataEnvelope.Wrap(Contents);
        var scrambled = DataEnvelope.Wrap(Contents, scrambleKey: 0x1234ABCD);

        Assert.Equal(plain.AsSpan(2, 4).ToArray(), scrambled.AsSpan(2, 4).ToArray());
        Assert.NotEqual(Contents, scrambled.AsSpan(10).ToArray());
    }

    [Fact]
    public void FlagsTheClientIgnoresAreIgnored()
    {
        var envelope = DataEnvelope.Wrap(Contents);
        envelope[1] = 0xFC;

        Assert.Equal(PacketError.None, DataEnvelope.TryUnwrap(envelope, Limit, out var contents));
        Assert.Equal(Contents, contents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void AnEnvelopeShorterThanItsHeaderIsRefused(int length)
    {
        var envelope = DataEnvelope.Wrap(Contents).AsSpan(0, length).ToArray();

        Assert.Equal(PacketError.Truncated, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void AnEnvelopeMissingAFieldItsFlagsPromiseIsRefused()
    {
        byte[] envelope = [(byte)'S', 0x03, 0, 0, 0, 0, 0x01, 0x02, 0x03, 0x04, 0x05];

        Assert.Equal(PacketError.Truncated, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void AnEnvelopeWithoutTheSignatureIsRefused()
    {
        var envelope = DataEnvelope.Wrap(Contents);
        envelope[0] = (byte)'T';

        Assert.Equal(PacketError.UnexpectedValue, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void ContentsThatDoNotMatchTheChecksumAreRefused()
    {
        var envelope = DataEnvelope.Wrap(Contents);
        envelope[^1] ^= 0xFF;

        Assert.Equal(PacketError.UnexpectedValue, DataEnvelope.TryUnwrap(envelope, Limit, out var contents));
        Assert.Empty(contents);
    }

    [Fact]
    public void ADeclaredSizeOverTheLimitIsRefusedBeforeAnythingIsAllocated()
    {
        // A four-gigabyte claim over a few bytes of payload: refused from the header alone.
        byte[] envelope = [(byte)'S', 0x01, 0, 0, 0, 0, .. Encoded.UInt32(uint.MaxValue), 0x78, 0x9C];

        Assert.Equal(PacketError.CountOutOfRange, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void PlainContentsOverTheLimitAreRefused()
    {
        Assert.Equal(PacketError.CountOutOfRange, DataEnvelope.TryUnwrap(DataEnvelope.Wrap(Contents), Contents.Length - 1, out _));
    }

    [Fact]
    public void AStreamThatInflatesPastItsDeclaredSizeIsRefused()
    {
        // A small envelope that expands to a megabyte: decompression stops at the size it
        // declares, so the bomb never goes off.
        var bomb = new byte[1 << 20];
        var envelope = DataEnvelope.Wrap(bomb, compress: true);
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(6), 1024);

        Assert.Equal(PacketError.UnexpectedValue, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void AStreamThatEndsBeforeItsDeclaredSizeIsRefused()
    {
        var envelope = DataEnvelope.Wrap(Contents, compress: true);
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(6), (uint)Contents.Length + 1);

        Assert.Equal(PacketError.UnexpectedValue, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void ACorruptStreamIsRefused()
    {
        byte[] envelope = [(byte)'S', 0x01, 0, 0, 0, 0, .. Encoded.UInt32(16), 0xFF, 0xFF, 0xFF, 0xFF];

        Assert.Equal(PacketError.UnexpectedValue, DataEnvelope.TryUnwrap(envelope, Limit, out _));
    }

    [Fact]
    public void ScramblingMatchesAnIndependentImplementation()
    {
        // Seventy zero bytes scrambled with key 0x1234ABCD by an implementation written
        // apart from this one: the payload is the keystream itself, crossing its 64-byte
        // repeat. A round trip alone would pass a keystream that is wrong both ways.
        const string Reference =
            "530200000000cdab34120c504796cd4bbb1a8e472f9f4f43a323103f17a8d13a8b2c9236ffb053327335142ee7"
            + "b9d5295b3e9625cfc257214347181db7cbd9182b509a149fd45b1013590c504796cd4b";

        Assert.Equal(Reference, Convert.ToHexString(DataEnvelope.Wrap(new byte[70], scrambleKey: 0x1234ABCD)).ToLowerInvariant());
    }

    [Fact]
    public void AnEnvelopeCompressedAndScrambledElsewhereUnwraps()
    {
        // "lobby " twelve times, compressed and then scrambled with 0x0BADF00D by the same
        // independent implementation.
        var reference = Convert.FromHexString("5303a01a31d40df0ad0b48000000b4d11546c24df84086a25e9c0f2b400771");

        Assert.Equal(PacketError.None, DataEnvelope.TryUnwrap(reference, Limit, out var contents));
        Assert.Equal(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("lobby ", 12))), contents);
    }

    [Fact]
    public void ACompressedPayloadIsAZlibStream()
    {
        // What the client inflates: a zlib stream, header and trailer included.
        var envelope = DataEnvelope.Wrap(Contents, compress: true);

        using var inflater = new ZLibStream(new MemoryStream(envelope[10..]), CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        inflater.CopyTo(inflated);

        Assert.Equal(Contents, inflated.ToArray());
    }
}
