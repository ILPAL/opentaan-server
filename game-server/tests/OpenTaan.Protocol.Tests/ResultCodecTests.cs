using OpenTaan.Protocol;
using OpenTaan.Protocol.Results;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// The end of a match: its result, the rewards on the result screen's cards, and the cards
/// players pick. Every value here is invented, each field's bytes unlike its neighbours',
/// and every expected body is written out by hand from the client's layout.
/// </summary>
public sealed class ResultCodecTests
{
    private static readonly GameResultSlot FullRow = new(
        SlotIndex: 0x00,
        CharacterLevel: 0x0102,
        ExpGained: -0x03040506,
        Levels: [new ExpResult(0x0708, 0x090A0B0C, 0x0D0E0F10, 0x11121314)],
        GoldEarned: 0x15161718,
        KillStat: 0x19,
        ForceStat: 0x1A1B1C1D,
        HitRate: 0.5f,
        SupportStat: 0x1E1F2021,
        Bonuses: [new ResultBonus(0x22, 0x23242526, 0x2728292A)],
        ForceMedalAward: 0x2B,
        KillMedalAward: 0x2C,
        SupportMedalAward: 0x2D,
        HasReward: true,
        ExpReward: 0x2E2F3031,
        GoldReward: 0x32333435);

    [Fact]
    public void AnEndIsTheResultThenAValue()
    {
        var end = new PcGameSessionEndGame(Result(winningTeam: 2), 0x36);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGameSessionEndGame.RegistryId),
            .. Encoded.UInt32(GameResult.RegistryId),
            0x02,
            .. Encoded.UInt32(8),
            .. FullRowBytes(),
            .. Enumerable.Range(1, 7).SelectMany(slot => EmptyRowBytes((byte)slot)),
            .. Encoded.UInt32(unchecked((uint)-0x37383940)),
            .. Encoded.UInt32(0x41424344),
            0x36,
        ];

        Assert.Equal(expected, Encoded.Bytes(end.Write));
        Assert.Equal(end, PcGameSessionEndGame.TryRead(expected).Packet);
    }

    [Fact]
    public void AResultIsWonByOneOfTheTwoTeams()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Result(winningTeam: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Result(winningTeam: 3));

        var body = Encoded.Bytes(new PcGameSessionEndGame(Result(winningTeam: 1), 0).Write);
        body[sizeof(uint) * 2] = 0x00;

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionEndGame.TryRead(body).Error);
    }

    [Fact]
    public void AResultRowsEverySlotInOrder()
    {
        Assert.Throws<ArgumentException>(() => new GameResult(1, [.. Rows().Take(7)], 0, 0));
        Assert.Throws<ArgumentException>(() => new GameResult(1, [.. Rows().Reverse()], 0, 0));

        // Seven rows on the wire, the eighth cut off along with the rest.
        byte[] body =
        [
            .. Encoded.UInt32(PcGameSessionEndGame.RegistryId),
            .. Encoded.UInt32(GameResult.RegistryId),
            0x01,
            .. Encoded.UInt32(7),
            .. Enumerable.Range(0, 7).SelectMany(slot => EmptyRowBytes((byte)slot)),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            0x00,
        ];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionEndGame.TryRead(body).Error);
    }

    [Fact]
    public void ARowForNoCharacterCarriesNoLevelAndNothingElse()
    {
        var written = Encoded.Bytes(new PcGameSessionEndGame(Result(winningTeam: 1), 0).Write);

        Assert.Equal(EmptyRowBytes(0x05), written.AsSpan(RowOffset(5), EmptyRowBytes(0x05).Length).ToArray());
        Assert.Equal(GameResultSlot.NoCharacter, GameResultSlot.Empty(0x05).CharacterLevel);
        Assert.Empty(GameResultSlot.Empty(0x05).Levels);
    }

    [Fact]
    public void ARowForACharacterThatEarnedNothingShowsItsLevelAndNoNumber()
    {
        var row = GameResultSlot.NothingEarned(0x03, 0x0405);

        Assert.Equal(0x0405, row.CharacterLevel);
        Assert.Equal(GameResultSlot.NoExperienceShown, row.ExpGained);
        Assert.Empty(row.Levels);
        Assert.False(row.HasReward);
    }

    [Fact]
    public void RewardsAreACountedListOfEightEachItsKindsClassNameCharacterThenWhatItGives()
    {
        var rewards = new PcResultSetReward(
        [
            new GameRewardExp("Kinh nghiệm", 0x01020304, 0x05060708),
            new GameRewardGold("Vàng", 0x090A0B0C, 0x0D0E0F10, true, 0x11121314),
            .. Enumerable.Range(0, 6).Select(_ => new GameRewardGold("Empty", 0, 0, false, 0)),
        ]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcResultSetReward.RegistryId),
            .. Encoded.UInt32(8),
            .. Encoded.UInt32(GameRewardExp.RegistryId), .. Encoded.Utf16("Kinh nghiệm"), 0x03, .. Encoded.UInt32(0x01020304), .. Encoded.UInt32(0x05060708),
            .. Encoded.UInt32(GameRewardGold.RegistryId), .. Encoded.Utf16("Vàng"), 0x04, .. Encoded.UInt32(0x090A0B0C), .. Encoded.UInt32(0x0D0E0F10), 0x01, .. Encoded.UInt32(0x11121314),
            .. Enumerable.Range(0, 6).SelectMany(_ => (byte[])
            [
                .. Encoded.UInt32(GameRewardGold.RegistryId), .. Encoded.Utf16("Empty"), 0x04, .. Encoded.UInt32(0), .. Encoded.UInt32(0), 0x00, .. Encoded.UInt32(0),
            ]),
        ];

        Assert.Equal(expected, Encoded.Bytes(rewards.Write));
        Assert.Equal(rewards, PcResultSetReward.TryRead(expected).Packet);
    }

    [Fact]
    public void AListOfRewardsHoldsExactlyEight()
    {
        Assert.Throws<ArgumentException>(() => new PcResultSetReward([new GameRewardExp("Exp", 1, 1)]));

        byte[] body =
        [
            .. Encoded.UInt32(PcResultSetReward.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GameRewardExp.RegistryId), .. Encoded.Utf16("Exp"), 0x03, .. Encoded.UInt32(1), .. Encoded.UInt32(1),
        ];

        Assert.Equal(PacketError.UnexpectedValue, PcResultSetReward.TryRead(body).Error);
    }

    [Fact]
    public void ARewardWhoseKindNumberDisagreesWithItsClassIsRefused()
    {
        var body = Encoded.Bytes(new PcResultSetReward([.. Enumerable.Range(0, 8).Select(_ => new GameRewardExp("Exp", 1, 1))]).Write);

        // The first reward's kind, after the list's count, the reward's id and its name.
        body[sizeof(uint) + sizeof(uint) + sizeof(uint) + Encoded.Utf16("Exp").Length] = 0x04;

        Assert.Equal(PacketError.UnexpectedValue, PcResultSetReward.TryRead(body).Error);
    }

    [Fact]
    public void ARewardOfAKindNotReadYetIsRefusedAsUnsupported()
    {
        byte[] body =
        [
            .. Encoded.UInt32(PcResultSetReward.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(RegistryIds.Of("GameRewardItem")), .. Encoded.Utf16("Item"), 0x02, .. Encoded.UInt32(1),
        ];

        Assert.Equal(PacketError.Unsupported, PcResultSetReward.TryRead(body).Error);
    }

    [Fact]
    public void ACardRequestIsTheCardThenTheSlotAndAnAutomaticOneIsTheSlotAlone()
    {
        byte[] selected = [.. Encoded.UInt32(PqResultSelectCard.RegistryId), 0x05, 0x06];
        byte[] random = [.. Encoded.UInt32(PqResultRandSelect.RegistryId), 0x06];

        Assert.Equal(selected, Encoded.Bytes(new PqResultSelectCard(0x05, 0x06).Write));
        Assert.Equal(new PqResultSelectCard(0x05, 0x06), PqResultSelectCard.TryRead(selected).Packet);
        Assert.Equal(random, Encoded.Bytes(new PqResultRandSelect(0x06).Write));
        Assert.Equal(new PqResultRandSelect(0x06), PqResultRandSelect.TryRead(random).Packet);
    }

    [Fact]
    public void ACardRequestIsReadWhateverCardAndSlotItNames()
    {
        byte[] body = [.. Encoded.UInt32(PqResultSelectCard.RegistryId), 0xFF, 0xFE];

        Assert.Equal(new PqResultSelectCard(0xFF, 0xFE), PqResultSelectCard.TryRead(body).Packet);
    }

    [Fact]
    public void ACardsAnswersAreTheCardThenTheSlot()
    {
        byte[] taken = [.. Encoded.UInt32(PrResultSelectCard.RegistryId), 0x07, 0x02];
        byte[] already = [.. Encoded.UInt32(PrResultAlreadyOpend.RegistryId), 0x07, 0x02];

        Assert.Equal(taken, Encoded.Bytes(new PrResultSelectCard(0x07, 0x02).Write));
        Assert.Equal(new PrResultSelectCard(0x07, 0x02), PrResultSelectCard.TryRead(taken).Packet);
        Assert.Equal(already, Encoded.Bytes(new PrResultAlreadyOpend(0x07, 0x02).Write));
        Assert.Equal(new PrResultAlreadyOpend(0x07, 0x02), PrResultAlreadyOpend.TryRead(already).Packet);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(0, 8)]
    public void ACardsAnswerOutsideTheCardsOrTheSlotsCanBeNeitherBuiltNorRead(byte card, byte slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrResultSelectCard(card, slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrResultAlreadyOpend(card, slot));
        Assert.Equal(PacketError.UnexpectedValue, PrResultSelectCard.TryRead([.. Encoded.UInt32(PrResultSelectCard.RegistryId), card, slot]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PrResultAlreadyOpend.TryRead([.. Encoded.UInt32(PrResultAlreadyOpend.RegistryId), card, slot]).Error);
    }

    // The full row in slot 0, then a row for no character in each other slot.
    private static IEnumerable<GameResultSlot> Rows() =>
        [FullRow, .. Enumerable.Range(1, 7).Select(slot => GameResultSlot.Empty((byte)slot))];

    private static GameResult Result(byte winningTeam) => new(winningTeam, [.. Rows()], -0x37383940, 0x41424344);

    private static byte[] FullRowBytes() =>
    [
        .. Encoded.UInt32(GameResultSlot.RegistryId),
        0x00,
        .. Encoded.UInt16(0x0102),
        .. Encoded.UInt32(unchecked((uint)-0x03040506)),
        .. Encoded.UInt32(1), .. Encoded.UInt32(ExpResult.RegistryId), .. Encoded.UInt16(0x0708), .. Encoded.UInt32(0x090A0B0C), .. Encoded.UInt32(0x0D0E0F10), .. Encoded.UInt32(0x11121314),
        .. Encoded.UInt32(0x15161718),
        0x19,
        .. Encoded.UInt32(0x1A1B1C1D),
        .. Encoded.UInt32(BitConverter.SingleToUInt32Bits(0.5f)),
        .. Encoded.UInt32(0x1E1F2021),
        .. Encoded.UInt32(1), .. Encoded.UInt32(ResultBonus.RegistryId), 0x22, .. Encoded.UInt32(0x23242526), .. Encoded.UInt32(0x2728292A),
        0x2B, 0x2C, 0x2D,
        0x01,
        .. Encoded.UInt32(0x2E2F3031),
        .. Encoded.UInt32(0x32333435),
    ];

    private static byte[] EmptyRowBytes(byte slot) =>
    [
        .. Encoded.UInt32(GameResultSlot.RegistryId),
        slot,
        .. Encoded.UInt16(0xFFFF),
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
        0x00,
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
        0x00, 0x00, 0x00,
        0x00,
        .. Encoded.UInt32(0),
        .. Encoded.UInt32(0),
    ];

    // Where slot's row starts in an end whose first row is the full one.
    private static int RowOffset(int slot) =>
        (3 * sizeof(uint)) + sizeof(byte) + FullRowBytes().Length + ((slot - 1) * EmptyRowBytes(0).Length);
}
