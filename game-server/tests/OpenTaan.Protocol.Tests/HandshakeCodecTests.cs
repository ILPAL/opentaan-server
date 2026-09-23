using OpenTaan.Protocol.Handshake;

namespace OpenTaan.Tests.Protocol;

/// <summary>The server's greeting. Every value here is invented.</summary>
public sealed class HandshakeCodecTests
{
    private static readonly PcFirstAccept Greeting = new(0x0123456789ABCDEF, 0x0F1E2D3C4B5A6978, 3);

    [Fact]
    public void WritesTheIdThenBothSeedsThenTheShift()
    {
        byte[] expected =
        [
            .. Encoded.UInt32(PcFirstAccept.RegistryId),
            0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
            0x78, 0x69, 0x5A, 0x4B, 0x3C, 0x2D, 0x1E, 0x0F,
            0x03,
        ];

        Assert.Equal(expected, Encoded.Bytes(Greeting.Write));
    }

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        var result = PcFirstAccept.TryRead(Encoded.Bytes(Greeting.Write));

        Assert.True(result.Succeeded);
        Assert.Equal(Greeting, result.Packet);
    }

    [Fact]
    public void PrintingNeverShowsTheSeeds()
    {
        var printed = Greeting.ToString();

        Assert.Equal("PcFirstAccept { Shift = 3 }", printed);
    }
}
