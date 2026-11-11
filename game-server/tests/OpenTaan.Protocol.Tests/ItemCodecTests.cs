using OpenTaan.Protocol;
using OpenTaan.Protocol.GamePlay;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// A match's items and ghosts: an item used, the items a shot spent, an item thrown away,
/// an item in use taken off, a box caught, a bonus, and where a ghost goes. Every value
/// here is invented, each field's bytes unlike its neighbours', and every expected body is
/// written out by hand from the client's layout.
/// </summary>
public sealed class ItemCodecTests
{
    private static readonly GameScenarioCommandEatItemBox Catch = new(0x03, 0x0B, 0x0C0D0E0F, 0x10111213, 0x1415161718191A1B, 0x1C1D, 0x1E1F2021);

    private static readonly GameScenarioCommandTeamGauge Gauge = new(0x04, 0x0C, 0x22232425, 0x26272829, 0x2A, 0x2B2C, 0x2D2E);

    [Fact]
    public void AnItemUsedIsTheSlotTheTeamWhomItIsAimedAtTheItemSlotThenTheItem()
    {
        var use = new PcGamePlayUseItem(0x05, 0x02, 0x07, 0x08, 0x09, 0x0A0B);
        byte[] expected = [.. Encoded.UInt32(PcGamePlayUseItem.RegistryId), 0x05, 0x02, 0x07, 0x08, 0x09, .. Encoded.UInt16(0x0A0B)];

        Assert.Equal(expected, Encoded.Bytes(use.Write));
        Assert.Equal(use, PcGamePlayUseItem.TryRead(expected).Packet);
    }

    [Fact]
    public void AShotsSpentItemsAreTheSlotTheTeamTheGhostTeamThenACountedListOfEachItemAndHowItWasSpent()
    {
        var spend = new PcGamePlaySpendItem(0x06, 0x01, 0x02, [new SpentItem(0x0304, 0x05), new SpentItem(0x0708, 0x09)]);
        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlaySpendItem.RegistryId),
            0x06,
            0x01,
            0x02,
            .. Encoded.UInt32(2),
            .. Encoded.UInt16(0x0304),
            0x05,
            .. Encoded.UInt16(0x0708),
            0x09,
        ];

        Assert.Equal(expected, Encoded.Bytes(spend.Write));
        Assert.Equal(spend, PcGamePlaySpendItem.TryRead(expected).Packet);
    }

    [Fact]
    public void ASpendMayNameNoCharacterAndNoTeamAndSpendNothing()
    {
        var spend = new PcGamePlaySpendItem(PcGamePlaySpendItem.NoSlot, 0xFF, 0xFF, []);
        byte[] expected = [.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0xFF, 0xFF, 0xFF, .. Encoded.UInt32(0)];

        Assert.Equal(expected, Encoded.Bytes(spend.Write));
        Assert.Equal(spend, PcGamePlaySpendItem.TryRead(expected).Packet);
    }

    [Fact]
    public void SpendsAreEqualOnlyForTheSameCharacterAndTeamsAndTheSameItemsInTheSameOrder()
    {
        PcGamePlaySpendItem Spend(byte slot, byte team, byte ghostTeam, params SpentItem[] items) => new(slot, team, ghostTeam, items);
        var first = new SpentItem(0x0304, 0x05);
        var second = new SpentItem(0x0708, 0x09);
        var spend = Spend(0x06, 0x01, 0x02, first, second);

        Assert.Equal(spend, Spend(0x06, 0x01, 0x02, first, second));
        Assert.Equal(spend.GetHashCode(), Spend(0x06, 0x01, 0x02, first, second).GetHashCode());
        Assert.NotEqual(spend, Spend(0x07, 0x01, 0x02, first, second));
        Assert.NotEqual(spend, Spend(0x06, 0x00, 0x02, first, second));
        Assert.NotEqual(spend, Spend(0x06, 0x01, 0x00, first, second));
        Assert.NotEqual(spend, Spend(0x06, 0x01, 0x02, second, first));
        Assert.NotEqual(spend, Spend(0x06, 0x01, 0x02, first));
    }

    [Fact]
    public void ASpendCountingMoreItemsThanItsBodyHoldsIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0x00, 0x00, 0x00, .. Encoded.UInt32(3), .. new byte[(3 * 3) - 1]];

        Assert.Equal(PacketError.CountOutOfRange, PcGamePlaySpendItem.TryRead(body).Error);
    }

    [Fact]
    public void AnItemThrownAwayIsTheSlotTheItemSlotThenTheItem()
    {
        var forsake = new PcGamePlayForsakeItem(0x04, 0x05, 0x0607);
        byte[] expected = [.. Encoded.UInt32(PcGamePlayForsakeItem.RegistryId), 0x04, 0x05, .. Encoded.UInt16(0x0607)];

        Assert.Equal(expected, Encoded.Bytes(forsake.Write));
        Assert.Equal(forsake, PcGamePlayForsakeItem.TryRead(expected).Packet);
    }

    [Fact]
    public void AnItemInUseTakenOffIsTheSlotTheTeamWhomItWasAimedAtThenTheItem()
    {
        var forsake = new PcGamePlayForsakeActiveItem(0x03, 0x01, 0x06, 0x07, 0x0809);
        byte[] expected = [.. Encoded.UInt32(PcGamePlayForsakeActiveItem.RegistryId), 0x03, 0x01, 0x06, 0x07, .. Encoded.UInt16(0x0809)];

        Assert.Equal(expected, Encoded.Bytes(forsake.Write));
        Assert.Equal(forsake, PcGamePlayForsakeActiveItem.TryRead(expected).Packet);
    }

    [Fact]
    public void ABoxCaughtIsTheSlotTheCatchThenAFlagAndTheGaugeChangeItFlags()
    {
        var plain = new PcGamePlayEatItemBox(0x02, Catch, TeamGauge: null);
        byte[] plainBody = [.. Encoded.UInt32(PcGamePlayEatItemBox.RegistryId), 0x02, .. CatchBytes(), 0x00];
        var gauged = new PcGamePlayEatItemBox(0x02, Catch, Gauge);
        byte[] gaugedBody = [.. Encoded.UInt32(PcGamePlayEatItemBox.RegistryId), 0x02, .. CatchBytes(), 0x01, .. GaugeBytes()];

        Assert.Equal(plainBody, Encoded.Bytes(plain.Write));
        Assert.Equal(plain, PcGamePlayEatItemBox.TryRead(plainBody).Packet);
        Assert.Equal(gaugedBody, Encoded.Bytes(gauged.Write));
        Assert.Equal(gauged, PcGamePlayEatItemBox.TryRead(gaugedBody).Packet);
    }

    [Fact]
    public void ABonusIsShapedAsABoxCaughtUnderAnIdOfItsOwn()
    {
        var plain = new PcGamePlayEatBonusItem(0x02, Catch, TeamGauge: null);
        byte[] plainBody = [.. Encoded.UInt32(PcGamePlayEatBonusItem.RegistryId), 0x02, .. CatchBytes(), 0x00];
        var gauged = new PcGamePlayEatBonusItem(0x02, Catch, Gauge);
        byte[] gaugedBody = [.. Encoded.UInt32(PcGamePlayEatBonusItem.RegistryId), 0x02, .. CatchBytes(), 0x01, .. GaugeBytes()];

        Assert.Equal(plainBody, Encoded.Bytes(plain.Write));
        Assert.Equal(plain, PcGamePlayEatBonusItem.TryRead(plainBody).Packet);
        Assert.Equal(gaugedBody, Encoded.Bytes(gauged.Write));
        Assert.Equal(gauged, PcGamePlayEatBonusItem.TryRead(gaugedBody).Packet);
        Assert.Equal(PacketError.WrongRegistryId, PcGamePlayEatItemBox.TryRead(plainBody).Error);
    }

    [Fact]
    public void ACatchIsACatchStepItsFlagIsZeroOrOneAndWhatItFlagsIsAGaugeChange()
    {
        // A box made where the catch belongs, a flag of 2, and a step of the base kind where the gauge change belongs.
        byte[] notACatch =
        [
            0x02,
            .. Encoded.StepHead(GameScenarioCommandMakeItemBox.RegistryId, 0x03, 0x0A, 0x0C0D0E0F, 0x10111213),
            .. Encoded.UInt64(0x1415161718191A1B),
            .. Encoded.Int32(0x1C1D1E1F),
            .. Encoded.Int32(0x20212223),
            0x00,
        ];
        byte[] badFlag = [0x02, .. CatchBytes(), 0x02, .. GaugeBytes()];
        byte[] notAGauge = [0x02, .. CatchBytes(), 0x01, .. Encoded.StepHead(GameScenarioCommand.RegistryId, 0x05, 0x08, 0x06070809, 0x0A0B0C0D)];

        foreach (var rest in new[] { notACatch, badFlag, notAGauge })
        {
            Assert.Equal(PacketError.UnexpectedValue, PcGamePlayEatItemBox.TryRead([.. Encoded.UInt32(PcGamePlayEatItemBox.RegistryId), .. rest]).Error);
            Assert.Equal(PacketError.UnexpectedValue, PcGamePlayEatBonusItem.TryRead([.. Encoded.UInt32(PcGamePlayEatBonusItem.RegistryId), .. rest]).Error);
        }
    }

    [Fact]
    public void AGhostsTargetIsTheSlotThenWhereItGoes()
    {
        var aim = new PcGamePlaySetGhostTarget(0x02, -0x03040506, 0x0708090A);
        byte[] expected = [.. Encoded.UInt32(PcGamePlaySetGhostTarget.RegistryId), 0x02, .. Encoded.Int32(-0x03040506), .. Encoded.Int32(0x0708090A)];

        Assert.Equal(expected, Encoded.Bytes(aim.Write));
        Assert.Equal(aim, PcGamePlaySetGhostTarget.TryRead(expected).Packet);
    }

    [Fact]
    public void ASlotOutsideTheRoomCanBeNeitherBuiltNorReadInAnyItemOrGhostPacket()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayUseItem(8, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySpendItem(8, 0, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySpendItem(0xFE, 0, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayForsakeItem(8, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayForsakeActiveItem(8, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayEatItemBox(8, Catch, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayEatBonusItem(8, Catch, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySetGhostTarget(8, 0, 0));

        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayUseItem.TryRead([.. Encoded.UInt32(PcGamePlayUseItem.RegistryId), 0x08, .. new byte[6]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySpendItem.TryRead([.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0x08, 0x00, 0x00, .. Encoded.UInt32(0)]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySpendItem.TryRead([.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0xFE, 0x00, 0x00, .. Encoded.UInt32(0)]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayForsakeItem.TryRead([.. Encoded.UInt32(PcGamePlayForsakeItem.RegistryId), 0x08, .. new byte[3]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayForsakeActiveItem.TryRead([.. Encoded.UInt32(PcGamePlayForsakeActiveItem.RegistryId), 0x08, .. new byte[5]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayEatItemBox.TryRead([.. Encoded.UInt32(PcGamePlayEatItemBox.RegistryId), 0x08, .. CatchBytes(), 0x00]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayEatBonusItem.TryRead([.. Encoded.UInt32(PcGamePlayEatBonusItem.RegistryId), 0x08, .. CatchBytes(), 0x00]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySetGhostTarget.TryRead([.. Encoded.UInt32(PcGamePlaySetGhostTarget.RegistryId), 0x08, .. new byte[8]]).Error);
    }

    [Fact]
    public void ATeamIsOneOfTheThreeTeamNumbersOrWhereThePacketAllowsNoTeamFF()
    {
        // An item used names its character's team, which is always one of the three.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayUseItem(0, 3, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayUseItem(0, 0xFF, 0, 0, 0, 0));
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayUseItem.TryRead([.. Encoded.UInt32(PcGamePlayUseItem.RegistryId), 0x00, 0x03, .. new byte[5]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayUseItem.TryRead([.. Encoded.UInt32(PcGamePlayUseItem.RegistryId), 0x00, 0xFF, .. new byte[5]]).Error);

        // An item taken off and a spend may name no team.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayForsakeActiveItem(0, 3, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySpendItem(0, 3, 0xFF, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySpendItem(0, 0xFF, 3, []));
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayForsakeActiveItem.TryRead([.. Encoded.UInt32(PcGamePlayForsakeActiveItem.RegistryId), 0x00, 0x03, .. new byte[4]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySpendItem.TryRead([.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0x00, 0x03, 0xFF, .. Encoded.UInt32(0)]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySpendItem.TryRead([.. Encoded.UInt32(PcGamePlaySpendItem.RegistryId), 0x00, 0xFF, 0x03, .. Encoded.UInt32(0)]).Error);

        var teamless = new PcGamePlayForsakeActiveItem(0x03, 0xFF, 0x06, 0x07, 0x0809);
        Assert.Equal(teamless, PcGamePlayForsakeActiveItem.TryRead(Encoded.Bytes(teamless.Write)).Packet);
    }

    [Fact]
    public void ABodyCutShortOrWithBytesLeftOverIsRefused()
    {
        (byte[] Body, Func<byte[], PacketError> Read)[] packets =
        [
            (Encoded.Bytes(new PcGamePlayUseItem(0x05, 0x02, 0x07, 0x08, 0x09, 0x0A0B).Write), body => PcGamePlayUseItem.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlaySpendItem(0x06, 0x01, 0x02, []).Write), body => PcGamePlaySpendItem.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlayForsakeItem(0x04, 0x05, 0x0607).Write), body => PcGamePlayForsakeItem.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlayForsakeActiveItem(0x03, 0x01, 0x06, 0x07, 0x0809).Write), body => PcGamePlayForsakeActiveItem.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlayEatItemBox(0x02, Catch, Gauge).Write), body => PcGamePlayEatItemBox.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlayEatBonusItem(0x02, Catch, null).Write), body => PcGamePlayEatBonusItem.TryRead(body).Error),
            (Encoded.Bytes(new PcGamePlaySetGhostTarget(0x02, 0x03040506, 0x0708090A).Write), body => PcGamePlaySetGhostTarget.TryRead(body).Error),
        ];

        foreach (var (body, read) in packets)
        {
            Assert.Equal(PacketError.Truncated, read(body[..^1]));
            Assert.Equal(PacketError.TrailingBytes, read([.. body, 0x00]));
        }
    }

    private static byte[] CatchBytes() =>
    [
        .. Encoded.StepHead(GameScenarioCommandEatItemBox.RegistryId, 0x03, 0x0B, 0x0C0D0E0F, 0x10111213),
        .. Encoded.UInt64(0x1415161718191A1B),
        .. Encoded.UInt16(0x1C1D),
        .. Encoded.UInt32(0x1E1F2021),
    ];

    private static byte[] GaugeBytes() =>
    [
        .. Encoded.StepHead(GameScenarioCommandTeamGauge.RegistryId, 0x04, 0x0C, 0x22232425, 0x26272829),
        0x2A,
        .. Encoded.UInt16(0x2B2C),
        .. Encoded.UInt16(0x2D2E),
    ];
}
