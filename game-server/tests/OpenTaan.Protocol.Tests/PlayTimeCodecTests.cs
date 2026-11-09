using OpenTaan.Protocol;
using OpenTaan.Protocol.PlayTime;

namespace OpenTaan.Tests.Protocol;

/// <summary>The packets that tell the client how long its player may play. Every value here is invented.</summary>
public sealed class PlayTimeCodecTests
{
    [Fact]
    public void AnAccountSlotsLimitIsTheSlotThenTheFourTimesThenTheRatio()
    {
        var limit = new PcNotifyPlayableTime(
            SlotIndex: 0x11,
            Timestamp: 0x55443322,
            PlayedSeconds: 0x99887766,
            AllowedSeconds: 0xDDCCBBAA,
            MaxPlaySeconds: 0x04030201,
            ProgressRatio: 0.5f);

        byte[] expected =
        [
            .. Encoded.UInt32(PcNotifyPlayableTime.RegistryId), 0x11,
            0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x3F,
        ];

        Assert.Equal(expected, Encoded.Bytes(limit.Write));
        Assert.Equal(limit, PcNotifyPlayableTime.TryRead(expected).Packet);
    }

    [Fact]
    public void ACharactersCounterIsTheCharacterThenThePlayedTimeThenWhetherItCounts()
    {
        var counter = new PcPlayableTimeCtrl(CharacterId: 0x44332211, PlayedSeconds: 0x88776655, Counting: true);

        byte[] expected = [.. Encoded.UInt32(PcPlayableTimeCtrl.RegistryId), 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x01];

        Assert.Equal(expected, Encoded.Bytes(counter.Write));
        Assert.Equal(counter, PcPlayableTimeCtrl.TryRead(expected).Packet);
    }

    [Fact]
    public void AResetIsItsIdAlone()
    {
        byte[] expected = Encoded.UInt32(PcResetPlayableTime.RegistryId);

        Assert.Equal(expected, Encoded.Bytes(new PcResetPlayableTime().Write));
        Assert.NotNull(PcResetPlayableTime.TryRead(expected).Packet);
    }

    [Fact]
    public void ABodyCutShortOrRunningOnIsRefused()
    {
        var limit = Encoded.Bytes(new PcNotifyPlayableTime(0, 1, 2, 3, 4, 0).Write);
        var counter = Encoded.Bytes(new PcPlayableTimeCtrl(1, 2, false).Write);
        var reset = Encoded.Bytes(new PcResetPlayableTime().Write);

        Assert.Equal(PacketError.Truncated, PcNotifyPlayableTime.TryRead(limit[..^1]).Error);
        Assert.Equal(PacketError.Truncated, PcPlayableTimeCtrl.TryRead(counter[..^1]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcNotifyPlayableTime.TryRead([.. limit, 0x00]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcPlayableTimeCtrl.TryRead([.. counter, 0x00]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcResetPlayableTime.TryRead([.. reset, 0x00]).Error);
    }

    [Fact]
    public void ACounterWhoseFlagIsNeitherOnNorOffIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcPlayableTimeCtrl.RegistryId), 0, 0, 0, 0, 0, 0, 0, 0, 0x02];

        Assert.Equal(PacketError.UnexpectedValue, PcPlayableTimeCtrl.TryRead(body).Error);
    }
}
