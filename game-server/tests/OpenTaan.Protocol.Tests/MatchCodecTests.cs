using OpenTaan.Protocol;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// Starting a match, loading it, a mission's levels, the start of a turn, giving up and
/// leaving. Every value here is invented, each field's bytes unlike its neighbours', and
/// every expected body is written out by hand from the client's layout.
/// </summary>
public sealed class MatchCodecTests
{
    private static readonly GameSlotPlayInfo FirstPlayer = new(0x01, 0x02030405, 0x06070809, true, 0x0A0B0C0D, 0x0E0F1011, 0x12131415, 0x1617, 0x1819, 0x1A1B, 0x1C1D);

    private static readonly GameSlotPlayInfo SecondPlayer = new(0x04, 0x02030405, 0x06070809, false, 0x0A0B0C0D, 0x0E0F1011, 0x12131415, 0x1617, 0x1819, 0x1A1B, 0x1C1D);

    [Fact]
    public void AStartRequestIsItsIdAlone()
    {
        byte[] expected = [.. Encoded.UInt32(PqGameSessionStartGame.RegistryId)];

        Assert.Equal(expected, Encoded.Bytes(new PqGameSessionStartGame().Write));
        Assert.True(PqGameSessionStartGame.TryRead(expected).Succeeded);
    }

    [Fact]
    public void AStartIsTheMapThePlayersTheWindAValueTheCaptainsThenTheSeed()
    {
        var start = new PcGameSessionStartGame(0x2122, [FirstPlayer, SecondPlayer], 0x23242526, 0x2728, [0x29, 0x2A, 0x2B], 0x2C2D2E2F);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGameSessionStartGame.RegistryId),
            .. Encoded.UInt16(0x2122),
            .. Encoded.UInt32(2),
            .. Player(0x01, 0x01),
            .. Player(0x04, 0x00),
            .. Encoded.UInt32(0x23242526),
            .. Encoded.UInt16(0x2728),
            0x29, 0x2A, 0x2B,
            .. Encoded.UInt32(0x2C2D2E2F),
        ];

        Assert.Equal(expected, Encoded.Bytes(start.Write));
        Assert.Equal(start, PcGameSessionStartGame.TryRead(expected).Packet);
    }

    [Fact]
    public void AStartNamingOneSlotTwiceCanBeNeitherBuiltNorRead()
    {
        Assert.Throws<ArgumentException>(() => new PcGameSessionStartGame(1, [FirstPlayer, FirstPlayer], 0, 0, [0, 1, 0], 0));

        byte[] body =
        [
            .. Encoded.UInt32(PcGameSessionStartGame.RegistryId),
            .. Encoded.UInt16(1),
            .. Encoded.UInt32(2),
            .. Player(0x01, 0x01),
            .. Player(0x01, 0x01),
            .. Encoded.UInt32(0),
            .. Encoded.UInt16(0),
            0x00, 0x01, 0x00,
            .. Encoded.UInt32(0),
        ];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionStartGame.TryRead(body).Error);
    }

    [Fact]
    public void AStartHasOneCaptainEntryForEachTeamNumber()
    {
        Assert.Throws<ArgumentException>(() => new PcGameSessionStartGame(1, [], 0, 0, [0, 1], 0));
    }

    [Fact]
    public void APlayerInASlotOutsideTheRoomCanBeNeitherBuiltNorRead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSlotPlayInfo(8, 0, 0, false, 0, 0, 0, 0, 0, 0, 0));

        byte[] body =
        [
            .. Encoded.UInt32(PcGameSessionStartGame.RegistryId),
            .. Encoded.UInt16(1),
            .. Encoded.UInt32(1),
            .. Player(0x08, 0x01),
            .. Encoded.UInt32(0),
            .. Encoded.UInt16(0),
            0x00, 0x01, 0x00,
            .. Encoded.UInt32(0),
        ];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionStartGame.TryRead(body).Error);
    }

    [Fact]
    public void AnEnemyReportIsACountedListOfCharactersEachWithAValue()
    {
        var report = new PqGameSessionReportEnemies([new ReportedEnemy(0x01020304, 0x05060708), new ReportedEnemy(0x090A0B0C, 0x0D0E0F10)]);

        byte[] expected =
        [
            .. Encoded.UInt32(PqGameSessionReportEnemies.RegistryId),
            .. Encoded.UInt32(2),
            .. Encoded.UInt32(0x01020304), .. Encoded.UInt32(0x05060708),
            .. Encoded.UInt32(0x090A0B0C), .. Encoded.UInt32(0x0D0E0F10),
        ];

        Assert.Equal(expected, Encoded.Bytes(report.Write));
        Assert.Equal(report, PqGameSessionReportEnemies.TryRead(expected).Packet);
    }

    [Fact]
    public void AnEnemyReportNamingMoreCharactersThanARoomHoldsIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqGameSessionReportEnemies.RegistryId), .. Encoded.UInt32(9), .. new byte[9 * 8]];

        Assert.Equal(PacketError.CountOutOfRange, PqGameSessionReportEnemies.TryRead(body).Error);
    }

    [Fact]
    public void ALoadingReportIsAOneByteCountThenEachSlotWithWhereItsCharacterStands()
    {
        var done = new PcGamePlayLoadingDone([new LoadedCharacter(0x01, 0x02030405, 0x06070809), new LoadedCharacter(0x05, -0x0A0B0C0D, 0x0E0F1011)]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlayLoadingDone.RegistryId),
            0x02,
            0x01, .. Encoded.UInt32(0x02030405), .. Encoded.UInt32(0x06070809),
            0x05, .. Encoded.UInt32(unchecked((uint)-0x0A0B0C0D)), .. Encoded.UInt32(0x0E0F1011),
        ];

        Assert.Equal(expected, Encoded.Bytes(done.Write));
        Assert.Equal(done, PcGamePlayLoadingDone.TryRead(expected).Packet);
    }

    [Fact]
    public void ALoadingReportListingMoreCharactersThanAConnectionPlaysIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcGamePlayLoadingDone.RegistryId), 0x03, .. new byte[3 * 9]];

        Assert.Equal(PacketError.CountOutOfRange, PcGamePlayLoadingDone.TryRead(body).Error);
        Assert.Throws<ArgumentException>(() => new PcGamePlayLoadingDone([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)]));
    }

    [Fact]
    public void ALoadingReportForASlotOutsideTheRoomIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcGamePlayLoadingDone.RegistryId), 0x01, 0x08, .. new byte[8]];

        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayLoadingDone.TryRead(body).Error);
    }

    [Fact]
    public void ALevelRequestIsTheLevelTwoValuesThenAFlag()
    {
        var request = new PqGameSessionStartMissionLevel(0x02, 0x03040506, 0x0708090A, true);

        byte[] expected =
        [
            .. Encoded.UInt32(PqGameSessionStartMissionLevel.RegistryId),
            0x02,
            .. Encoded.UInt32(0x03040506),
            .. Encoded.UInt32(0x0708090A),
            0x01,
        ];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionStartMissionLevel.TryRead(expected).Packet);
    }

    [Fact]
    public void ARequestForTheLevelAfterTheLastCarriesNoLevelsValues()
    {
        var request = new PqGameSessionStartMissionLevel(
            PqGameSessionStartMissionLevel.NoNextLevel,
            PqGameSessionStartMissionLevel.NoValue,
            PqGameSessionStartMissionLevel.NoValue,
            false);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionStartMissionLevel.RegistryId), 0xFF, .. Encoded.UInt32(0xFFFFFFFF), .. Encoded.UInt32(0xFFFFFFFF), 0x00];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionStartMissionLevel.TryRead(expected).Packet);
    }

    [Fact]
    public void ALevelRequestsFlagOtherThanZeroOrOneIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqGameSessionStartMissionLevel.RegistryId), 0x01, .. new byte[8], 0x02];

        Assert.Equal(PacketError.UnexpectedValue, PqGameSessionStartMissionLevel.TryRead(body).Error);
    }

    [Fact]
    public void ALevelsAnnouncementAndItsReadyReportAreTheLevelAlone()
    {
        byte[] announced = [.. Encoded.UInt32(PcGameSessionStartMissionLevel.RegistryId), 0x03];
        byte[] ready = [.. Encoded.UInt32(PqGameSessionMissionLevelReady.RegistryId), 0x03];

        Assert.Equal(announced, Encoded.Bytes(new PcGameSessionStartMissionLevel(0x03).Write));
        Assert.Equal(new PcGameSessionStartMissionLevel(0x03), PcGameSessionStartMissionLevel.TryRead(announced).Packet);
        Assert.Equal(ready, Encoded.Bytes(new PqGameSessionMissionLevelReady(0x03).Write));
        Assert.Equal(new PqGameSessionMissionLevelReady(0x03), PqGameSessionMissionLevelReady.TryRead(ready).Packet);
    }

    [Fact]
    public void ATurnStartIsTheSlotTheTurnsValuesTheThreeListsTheDropQualityThenTheRound()
    {
        var turn = new PcGamePlayTurnStart(
            TurnSlot: 0x01,
            Field11: 0x02,
            Wind: 0x03040506,
            AmmoAddition: 0x0708,
            ItemBoxBudget: 0x09,
            RemovedItemBoxes: [0x0A0B0C0D0E0F1011],
            GhostMoves: [new GhostMove(0x02, 0x12131415, 0x16171819, -0x1A1B1C1D, 0x1E1F2021)],
            HpUpdates: [new HpUpdate(0x03, 0x22, -0x23242526), new HpUpdate(0x05, 0x27, 0x28292A2B)],
            DropQuality: 0x2C2D2E2F,
            DropQualityByTeam: [0x30313233, 0x34353637, 0x38393A3B],
            MissionRound: 0x3C3D);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlayTurnStart.RegistryId),
            0x01,
            0x02,
            .. Encoded.UInt32(0x03040506),
            .. Encoded.UInt16(0x0708),
            0x09,
            .. Encoded.UInt32(1), .. Encoded.UInt32(0x0E0F1011), .. Encoded.UInt32(0x0A0B0C0D),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GhostMove.RegistryId), 0x02,
            .. Encoded.UInt32(0x12131415), .. Encoded.UInt32(0x16171819), .. Encoded.UInt32(unchecked((uint)-0x1A1B1C1D)), .. Encoded.UInt32(0x1E1F2021),
            .. Encoded.UInt32(2),
            .. Encoded.UInt32(HpUpdate.RegistryId), 0x03, 0x22, .. Encoded.UInt32(unchecked((uint)-0x23242526)),
            .. Encoded.UInt32(HpUpdate.RegistryId), 0x05, 0x27, .. Encoded.UInt32(0x28292A2B),
            .. Encoded.UInt32(0x2C2D2E2F),
            .. Encoded.UInt32(0x30313233), .. Encoded.UInt32(0x34353637), .. Encoded.UInt32(0x38393A3B),
            .. Encoded.UInt16(0x3C3D),
        ];

        Assert.Equal(expected, Encoded.Bytes(turn.Write));
        Assert.Equal(turn, PcGamePlayTurnStart.TryRead(expected).Packet);
    }

    [Fact]
    public void ATurnForASlotOutsideTheRoomCanBeNeitherBuiltNorRead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayTurnStart(8, 0, 0, 0, 0, [], [], [], 0, [0, 0, 0], 0));

        byte[] body =
        [
            .. Encoded.UInt32(PcGamePlayTurnStart.RegistryId),
            0x08, 0x00, .. Encoded.UInt32(0), .. Encoded.UInt16(0), 0x00,
            .. Encoded.UInt32(0), .. Encoded.UInt32(0), .. Encoded.UInt32(0),
            .. Encoded.UInt32(0), .. Encoded.UInt32(0), .. Encoded.UInt32(0), .. Encoded.UInt32(0),
            .. Encoded.UInt16(0),
        ];

        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayTurnStart.TryRead(body).Error);
    }

    [Fact]
    public void GivingUpIsItsIdAlone()
    {
        byte[] expected = [.. Encoded.UInt32(PcGamePlayGiveUp.RegistryId)];

        Assert.Equal(expected, Encoded.Bytes(new PcGamePlayGiveUp().Write));
        Assert.True(PcGamePlayGiveUp.TryRead(expected).Succeeded);
    }

    [Fact]
    public void ALeaverIsTheirSlot()
    {
        byte[] expected = [.. Encoded.UInt32(PcGamePlayLeaveUser.RegistryId), 0x05];

        Assert.Equal(expected, Encoded.Bytes(new PcGamePlayLeaveUser(0x05).Write));
        Assert.Equal(new PcGamePlayLeaveUser(0x05), PcGamePlayLeaveUser.TryRead(expected).Packet);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayLeaveUser(8));
    }

    [Fact]
    public void ABodyCutShortOrWithBytesLeftOverIsRefused()
    {
        byte[] ready = [.. Encoded.UInt32(PqGameSessionMissionLevelReady.RegistryId), 0x03];

        Assert.Equal(PacketError.Truncated, PqGameSessionMissionLevelReady.TryRead(ready.AsSpan(0, ready.Length - 1)).Error);
        Assert.Equal(PacketError.TrailingBytes, PqGameSessionMissionLevelReady.TryRead([.. ready, 0x00]).Error);
        Assert.Equal(PacketError.TrailingBytes, PcGamePlayGiveUp.TryRead([.. Encoded.UInt32(PcGamePlayGiveUp.RegistryId), 0x00]).Error);
    }

    // A player's entry with the invented values of FirstPlayer, in the slot and with the
    // airborne flag given.
    private static byte[] Player(byte slot, byte airborne) =>
    [
        .. Encoded.UInt32(GameSlotPlayInfo.RegistryId),
        slot,
        .. Encoded.UInt32(0x02030405),
        .. Encoded.UInt32(0x06070809),
        airborne,
        .. Encoded.UInt32(0x0A0B0C0D),
        .. Encoded.UInt32(0x0E0F1011),
        .. Encoded.UInt32(0x12131415),
        .. Encoded.UInt16(0x1617),
        .. Encoded.UInt16(0x1819),
        .. Encoded.UInt16(0x1A1B),
        .. Encoded.UInt16(0x1C1D),
    ];
}
