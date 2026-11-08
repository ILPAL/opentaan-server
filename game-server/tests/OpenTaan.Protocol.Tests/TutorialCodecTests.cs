using OpenTaan.Protocol;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;

namespace OpenTaan.Tests.Protocol;

/// <summary>The packets the client sends while it plays the tutorial. Every value here is invented.</summary>
public sealed class TutorialCodecTests
{
    [Fact]
    public void APickupReportIsTheGameSlotThenTheItemSlotThenTheItem()
    {
        var report = new PcGamePlayNotifyEatItem(SlotIndex: 0x11, ItemSlot: 0x22, ItemId: 0x4433);

        byte[] expected = [.. Encoded.UInt32(PcGamePlayNotifyEatItem.RegistryId), 0x11, 0x22, 0x33, 0x44];

        Assert.Equal(expected, Encoded.Bytes(report.Write));
        Assert.Equal(report, PcGamePlayNotifyEatItem.TryRead(expected).Packet);
    }

    [Fact]
    public void AGhostPositionIsTheGameSlotThenBothCoordinatesThenTheDirection()
    {
        var position = new PcGamePlaySetGhostPos(SlotIndex: 0x11, X: -1000, Y: 0x55443322, Direction: 0x66);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlaySetGhostPos.RegistryId), 0x11, 0x18, 0xFC, 0xFF, 0xFF, 0x22, 0x33, 0x44, 0x55, 0x66,
        ];

        Assert.Equal(expected, Encoded.Bytes(position.Write));
        Assert.Equal(position, PcGamePlaySetGhostPos.TryRead(expected).Packet);
    }

    [Fact]
    public void AFinishedTutorialCarriesOneCharacterId()
    {
        var report = new PcTutorialFinished(0x44332211);

        byte[] expected = [.. Encoded.UInt32(PcTutorialFinished.RegistryId), 0x11, 0x22, 0x33, 0x44];

        Assert.Equal(expected, Encoded.Bytes(report.Write));
        Assert.Equal(report, PcTutorialFinished.TryRead(expected).Packet);
    }

    [Fact]
    public void ABodyCutShortOrRunningOnIsRefused()
    {
        var pickup = Encoded.Bytes(new PcGamePlayNotifyEatItem(0, 0, 1).Write);
        var ghost = Encoded.Bytes(new PcGamePlaySetGhostPos(0, 1, 2, 3).Write);
        var finished = Encoded.Bytes(new PcTutorialFinished(1).Write);

        Assert.Equal(PacketError.Truncated, PcGamePlayNotifyEatItem.TryRead(pickup[..^1]).Error);
        Assert.Equal(PacketError.Truncated, PcGamePlaySetGhostPos.TryRead(ghost[..^1]).Error);
        Assert.Equal(PacketError.Truncated, PcTutorialFinished.TryRead(finished[..^1]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcGamePlayNotifyEatItem.TryRead([.. pickup, 0x00]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcGamePlaySetGhostPos.TryRead([.. ghost, 0x00]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcTutorialFinished.TryRead([.. finished, 0x00]).Error);
    }
}
