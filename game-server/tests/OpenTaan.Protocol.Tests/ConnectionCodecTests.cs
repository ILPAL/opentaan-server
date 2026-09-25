using OpenTaan.Protocol;
using OpenTaan.Protocol.Connection;

namespace OpenTaan.Tests.Protocol;

/// <summary>The packets that open and keep up a connection. Every value here is invented.</summary>
public sealed class ConnectionCodecTests
{
    [Fact]
    public void TheFirstPacketAnswerIsTheStatusThenTheValueThenTheTrailingByte()
    {
        // Three different values, so a field written in the wrong place shows.
        var answer = new PrFirstPacket(0x11, 0x44332211, 0x01);

        byte[] expected = [.. Encoded.UInt32(PrFirstPacket.RegistryId), 0x11, 0x11, 0x22, 0x33, 0x44, 0x01];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrFirstPacket.TryRead(expected).Packet);
    }

    [Fact]
    public void TheFirstPacketAnswerSendsZeroInTheTrailingByteUnlessToldOtherwise()
    {
        var written = Encoded.Bytes(new PrFirstPacket(0, 1).Write);

        Assert.Equal(0, written[^1]);
    }

    [Fact]
    public void AFirstPacketAnswerCutShortIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PrFirstPacket.RegistryId), 0x00, 0x01, 0x00];

        Assert.Equal(PacketError.Truncated, PrFirstPacket.TryRead(body).Error);
    }
}
