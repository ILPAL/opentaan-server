using OpenTaan.Protocol;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

/// <summary>The packets that tell a connection its character's experience and money, as the client reads them. Every value is invented.</summary>
public sealed class ProgressCodecTests
{
    [Fact]
    public void AnExperienceUpdateIsTheAccountSlotThenTheLevelTheExperienceAndTheLevelsBar()
    {
        var packet = new PcUpdateExp(0x01, 0x0203, 0x04050607, 0x08090A0B, 0x0C0D0E0F);

        byte[] expected =
        [
            .. Encoded.UInt32(PcUpdateExp.RegistryId), 0x01, .. Encoded.UInt16(0x0203),
            .. Encoded.UInt32(0x04050607), .. Encoded.UInt32(0x08090A0B), .. Encoded.UInt32(0x0C0D0E0F),
        ];
        Assert.Equal(expected, Encoded.Bytes(packet.Write));
        Assert.Equal(packet, PcUpdateExp.TryRead(expected).Packet);
    }

    [Fact]
    public void AMoneyUpdateIsTheAccountSlotTheKindTheBalanceAndTheLimitTheChatMayName()
    {
        var packet = new PcUpdateMoney(0x01, MoneyKind.LeagueMoney, 0x03040506, 0x0708090A, ShowsLimit: true);

        byte[] expected =
        [
            .. Encoded.UInt32(PcUpdateMoney.RegistryId), 0x01, 0x02,
            .. Encoded.UInt32(0x03040506), .. Encoded.UInt32(0x0708090A), 0x01,
        ];
        Assert.Equal(expected, Encoded.Bytes(packet.Write));
        Assert.Equal(packet, PcUpdateMoney.TryRead(expected).Packet);
    }

    [Fact]
    public void GoldIsKindZeroWithNothingSaidInTheChat() =>
        Assert.Equal(
            [.. Encoded.UInt32(PcUpdateMoney.RegistryId), 0x01, 0x00, .. Encoded.UInt32(180), .. Encoded.UInt32(0), 0x00],
            Encoded.Bytes(PcUpdateMoney.GoldOf(1, 180).Write));

    [Fact]
    public void AMoneyUpdateOfAKindTheClientHasNoneOfIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcUpdateMoney.RegistryId), 0x00, 0x03, .. Encoded.UInt32(1), .. Encoded.UInt32(0), 0x00];

        var read = PcUpdateMoney.TryRead(body);

        Assert.Null(read.Packet);
        Assert.Equal(PacketError.UnexpectedValue, read.Error);
    }
}
