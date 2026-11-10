using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// Rooms: their record, slots and host, the requests to create, enter, quick-join and
/// leave one and their answers, the changes a room is told of, and the lobby's room list
/// and room info. Every value here is invented, each field's bytes unlike its
/// neighbours', and every expected body is written out by hand from the client's layout.
/// </summary>
public sealed class RoomCodecTests
{
    private static readonly GameSessionInfo Room = new(
        RoomNumber: 0x0A0B,
        Status: RoomStatus.Requested,
        Grade: 0x06,
        Name: "Phòng Một",
        GameType: GameType.Mission,
        GameMode: GameMode.CaptainNoItems,
        ChanceMode: ChanceMode.Bonus,
        CurrentPlayers: 0x07,
        MaxPlayers: 0x08,
        Password: "mật khẩu",
        MapId: 0x0C0D);

    // The record's order: the password comes before the map.
    private static readonly byte[] RoomBytes =
    [
        .. Encoded.UInt32(GameSessionInfo.RegistryId),
        .. Encoded.UInt16(0x0A0B),
        0x05,
        0x06,
        .. Encoded.Utf16("Phòng Một"),
        0x01,
        .. Encoded.UInt16(0x0003),
        0x02,
        0x07,
        0x08,
        0x01, .. Encoded.Utf16("mật khẩu"),
        .. Encoded.UInt16(0x0C0D),
    ];

    private static readonly GameSessionOption Option = new(Field08: 0x11223344, HostPlayerId: 0x55667788);

    private static readonly byte[] OptionBytes =
        [.. Encoded.UInt32(GameSessionOption.RegistryId), .. Encoded.UInt32(0x11223344), .. Encoded.UInt32(0x55667788)];

    private static readonly SlotOccupant Occupant = new(
        PlayerId: 0x01020304,
        CharacterId: 0x05060708,
        Name: "Kim",
        GuildName: "Rồng",
        Level: 0x090A,
        Team: 2,
        Appearance: CharacterSamples.Appearance,
        WeaponId: 0x0B0C);

    // The occupant's part of a slot: the appearance and the option list each behind a presence flag.
    private static readonly byte[] OccupantBytes =
    [
        .. Encoded.UInt32(0x01020304),
        .. Encoded.UInt32(0x05060708),
        .. Encoded.Utf16("Kim"),
        .. Encoded.Utf16("Rồng"),
        .. Encoded.UInt16(0x090A),
        0x02,
        0x01, .. CharacterSamples.AppearanceBytes,
        .. Encoded.UInt16(0x0B0C),
        0x01, .. Encoded.UInt32(OptionList.RegistryId), .. Encoded.UInt32(0),
    ];

    private static readonly IntegrityReport Integrity = new(0x11111111, 0x22222222, 0x33, 0x44444444, 0x55555555, 0x66666666);

    private static readonly byte[] IntegrityBytes =
    [
        .. Encoded.UInt32(0x11111111),
        .. Encoded.UInt32(0x22222222),
        0x33,
        .. Encoded.UInt32(0x44444444),
        .. Encoded.UInt32(0x55555555),
        .. Encoded.UInt32(0x66666666),
    ];

    private static byte[] SlotBytes(byte index, byte status, byte[] occupant) =>
        [.. Encoded.UInt32(GameSlotInfo.RegistryId), index, status, .. occupant];

    private static GameSlotInfo[] EverySlot() =>
        [.. Enumerable.Range(0, GameSlotInfo.SlotCount).Select(index => index switch
        {
            0 => GameSlotInfo.Taken(0, Occupant, ready: false),
            3 => GameSlotInfo.Closed(3),
            _ => GameSlotInfo.Open((byte)index),
        })];

    private static byte[] EverySlotBytes() =>
        [.. Enumerable.Range(0, GameSlotInfo.SlotCount).SelectMany(index => index switch
        {
            0 => SlotBytes(0, 0x02, OccupantBytes),
            3 => SlotBytes(3, 0x01, []),
            _ => SlotBytes((byte)index, 0x00, []),
        })];

    [Fact]
    public void ARoomCarriesItsFieldsInTheClientsOrderWithThePasswordBeforeTheMap()
    {
        var request = new PqGameSessionCreate(Room, Integrity);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionCreate.RegistryId), .. RoomBytes, .. IntegrityBytes];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionCreate.TryRead(expected).Packet);
    }

    [Fact]
    public void ARoomWithoutAPasswordCarriesTheClearedFlagAndNoText()
    {
        var request = new PqGameSessionCreate(Room with { Password = null }, Integrity);

        byte[] expected =
        [
            .. Encoded.UInt32(PqGameSessionCreate.RegistryId),
            .. RoomBytes[..^(1 + Encoded.Utf16("mật khẩu").Length + 2)],
            0x00,
            .. Encoded.UInt16(0x0C0D),
            .. IntegrityBytes,
        ];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Null(PqGameSessionCreate.TryRead(expected).Packet!.Room.Password);
    }

    [Fact]
    public void ARoomWithStatusZeroHoldsNoRoomAndIsNotRead()
    {
        // With status 0 the client writes the number and the status, and nothing else.
        byte[] empty = [.. Encoded.UInt32(PqGameSessionCreate.RegistryId), .. Encoded.UInt32(GameSessionInfo.RegistryId), .. Encoded.UInt16(0x0A0B), 0x00, .. IntegrityBytes];

        Assert.Equal(PacketError.Unsupported, PqGameSessionCreate.TryRead(empty).Error);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSessionInfo(0x0A0B, 0, 0x06, "x", GameType.TeamGame, GameMode.Items, ChanceMode.Basic, 0, 8, null, 0));
    }

    [Fact]
    public void ARoomPasswordFlagOtherThanZeroOrOneIsRefused()
    {
        var body = Encoded.Bytes(new PqGameSessionCreate(Room, Integrity).Write);
        body[sizeof(uint) + RoomBytes.Length - (Encoded.Utf16("mật khẩu").Length + 3)] = 0x02;

        Assert.Equal(PacketError.UnexpectedValue, PqGameSessionCreate.TryRead(body).Error);
    }

    [Fact]
    public void AnEnterRequestIsTheRoomNumberThePasswordThenTheIntegrityReport()
    {
        var request = new PqGameSessionEnter(0x0A0B, "mk", Integrity);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionEnter.RegistryId), .. Encoded.UInt16(0x0A0B), .. Encoded.Utf16("mk"), .. IntegrityBytes];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionEnter.TryRead(expected).Packet);
    }

    [Fact]
    public void AnEnterRequestCutShortInsideItsIntegrityReportIsRefused()
    {
        var body = Encoded.Bytes(new PqGameSessionEnter(0x0A0B, "mk", Integrity).Write);

        Assert.Equal(PacketError.Truncated, PqGameSessionEnter.TryRead(body[..^1]).Error);
    }

    [Fact]
    public void AQuickJoinRequestNamesAnyRoomThenCarriesTheIntegrityReport()
    {
        var request = new PqGameSessionQuickJoin(PqGameSessionQuickJoin.AnyRoom, Integrity);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionQuickJoin.RegistryId), 0xFF, 0xFF, .. IntegrityBytes];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionQuickJoin.TryRead(expected).Packet);
    }

    [Fact]
    public void ALeaveRequestIsTheRoomNumberThenTheReason()
    {
        var request = new PqGameSessionLeave(0x0A0B, 0x07);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionLeave.RegistryId), .. Encoded.UInt16(0x0A0B), 0x07];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionLeave.TryRead(expected).Packet);
    }

    [Fact]
    public void AnOccupiedSlotCarriesItsCharacterThenTheAppearanceAndAnEmptyOptionListEachBehindAFlag()
    {
        var update = new PcGameSessionUpdateSlot(SlotChange.Seated, GameSlotInfo.Taken(5, Occupant, ready: true), "Đội");

        byte[] expected = [.. Encoded.UInt32(PcGameSessionUpdateSlot.RegistryId), 0x00, .. SlotBytes(5, 0x03, OccupantBytes), .. Encoded.Utf16("Đội")];

        Assert.Equal(expected, Encoded.Bytes(update.Write));
        Assert.Equal(update, PcGameSessionUpdateSlot.TryRead(expected).Packet);
    }

    [Fact]
    public void AnEmptySlotIsItsIndexAndStatusAlone()
    {
        var update = new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(6), string.Empty);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionUpdateSlot.RegistryId), 0x01, .. SlotBytes(6, 0x00, []), .. Encoded.Utf16(string.Empty)];

        Assert.Equal(expected, Encoded.Bytes(update.Write));
        Assert.Equal(update, PcGameSessionUpdateSlot.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(0x08, 0x00)]
    [InlineData(0xFF, 0x00)]
    [InlineData(0x02, 0x04)]
    public void ASlotOutsideTheRoomOrWithAStatusTheClientDoesNotKnowIsRefused(byte index, byte status)
    {
        byte[] body = [.. Encoded.UInt32(PcGameSessionUpdateSlot.RegistryId), 0x00, .. SlotBytes(index, status, []), .. Encoded.Utf16(string.Empty)];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionUpdateSlot.TryRead(body).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ACharacterOnATeamOtherThanOneOrTwoIsRefused(byte team)
    {
        var occupant = OccupantBytes.ToArray();
        occupant[(2 * sizeof(uint)) + Encoded.Utf16("Kim").Length + Encoded.Utf16("Rồng").Length + sizeof(ushort)] = team;

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionUpdateSlot.TryRead(UpdateOf(SlotBytes(1, 0x02, occupant))).Error);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotOccupant(1, 2, "Kim", string.Empty, 1, team, CharacterSamples.Appearance, 0));
    }

    [Fact]
    public void AnOccupiedSlotWithoutAnAppearanceOrAnOptionListIsNotRead()
    {
        var appearanceFlag = (2 * sizeof(uint)) + Encoded.Utf16("Kim").Length + Encoded.Utf16("Rồng").Length + sizeof(ushort) + 1;
        byte[] withoutAppearance = [.. OccupantBytes[..appearanceFlag], 0x00, .. OccupantBytes[(appearanceFlag + 1 + CharacterSamples.AppearanceBytes.Length)..]];
        byte[] withoutOptions = [.. OccupantBytes[..^(1 + (2 * sizeof(uint)))], 0x00];
        byte[] withAnOption = [.. OccupantBytes[..^sizeof(uint)], .. Encoded.UInt32(1), .. new byte[16]];

        Assert.Equal(PacketError.Unsupported, PcGameSessionUpdateSlot.TryRead(UpdateOf(SlotBytes(1, 0x02, withoutAppearance))).Error);
        Assert.Equal(PacketError.Unsupported, PcGameSessionUpdateSlot.TryRead(UpdateOf(SlotBytes(1, 0x02, withoutOptions))).Error);
        Assert.Equal(PacketError.Unsupported, PcGameSessionUpdateSlot.TryRead(UpdateOf(SlotBytes(1, 0x02, withAnOption))).Error);
    }

    [Fact]
    public void ASlotHoldsACharacterExactlyWhenItsStatusSaysItIsTaken()
    {
        Assert.Throws<ArgumentException>(() => new GameSlotInfo(1, SlotStatus.Seated, null));
        Assert.Throws<ArgumentException>(() => new GameSlotInfo(1, SlotStatus.Open, Occupant));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameSlotInfo.Open(GameSlotInfo.SlotCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameSlotInfo(1, (SlotStatus)4, null));
    }

    [Fact]
    public void ASlotChangeTheClientDoesNotKnowCannotBeWritten()
    {
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PcGameSessionUpdateSlot((SlotChange)2, GameSlotInfo.Open(1), string.Empty).Write));
    }

    [Fact]
    public void ARoomReplyIsTheRoomItsHostEverySlotInOrderTheDifficultyAndTheTwoTeamNames()
    {
        var reply = new PcGameSessionInit(Room, Option, EverySlot(), 0x02, "Đội Một", "Đội Hai");

        byte[] expected =
        [
            .. Encoded.UInt32(PcGameSessionInit.RegistryId),
            .. RoomBytes,
            .. OptionBytes,
            .. Encoded.UInt32(GameSlotInfo.SlotCount), .. EverySlotBytes(),
            0x02,
            .. Encoded.Utf16("Đội Một"),
            .. Encoded.Utf16("Đội Hai"),
        ];

        Assert.Equal(expected, Encoded.Bytes(reply.Write));

        var read = PcGameSessionInit.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(Room, read.Packet.Room);
        Assert.Equal(Option, read.Packet.Option);
        Assert.Equal(EverySlot(), read.Packet.Slots);
        Assert.Equal(("Đội Một", "Đội Hai"), (read.Packet.TeamOneName, read.Packet.TeamTwoName));
    }

    [Fact]
    public void ARoomReplyThatDoesNotListEverySlotInOrderCanBeNeitherWrittenNorRead()
    {
        var slots = EverySlot();
        GameSlotInfo[] missingOne = slots[..^1];
        GameSlotInfo[] swapped = [slots[1], slots[0], .. slots[2..]];

        foreach (var wrong in new[] { missingOne, swapped })
        {
            var reply = new PcGameSessionInit(Room, Option, wrong, 0, string.Empty, string.Empty);
            Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(reply.Write));

            byte[] body =
            [
                .. Encoded.UInt32(PcGameSessionInit.RegistryId), .. RoomBytes, .. OptionBytes,
                .. Encoded.UInt32((uint)wrong.Length), .. wrong.SelectMany(slot => slot.Occupant is null ? SlotBytes(slot.SlotIndex, (byte)slot.Status, []) : SlotBytes(slot.SlotIndex, (byte)slot.Status, OccupantBytes)),
                0x00, .. Encoded.Utf16(string.Empty), .. Encoded.Utf16(string.Empty),
            ];
            Assert.Equal(PacketError.UnexpectedValue, PcGameSessionInit.TryRead(body).Error);
        }
    }

    [Fact]
    public void ARoomReplyCountingMoreSlotsThanItHoldsIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcGameSessionInit.RegistryId), .. RoomBytes, .. OptionBytes, .. Encoded.UInt32(0xFFFFFFFF)];

        Assert.Equal(PacketError.CountOutOfRange, PcGameSessionInit.TryRead(body).Error);
    }

    [Fact]
    public void ACreateRefusalIsOneByteAndCanNeverBeTheStatusOnWhichTheClientQuits()
    {
        byte[] expected = [.. Encoded.UInt32(PrGameSessionCreateFail.RegistryId), 0x01];

        Assert.Equal(expected, Encoded.Bytes(new PrGameSessionCreateFail(CreateRefusal.Refused).Write));
        Assert.Equal(CreateRefusal.Refused, PrGameSessionCreateFail.TryRead(expected).Packet!.Status);
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrGameSessionCreateFail((CreateRefusal)4).Write));
    }

    [Theory]
    [InlineData(EnterRefusal.AlreadyInRoom, 3)]
    [InlineData(EnterRefusal.NoSuchRoom, 4)]
    [InlineData(EnterRefusal.RoomFull, 5)]
    [InlineData(EnterRefusal.WrongPassword, 6)]
    [InlineData(EnterRefusal.AlreadyStarted, 7)]
    [InlineData(EnterRefusal.SlotUnavailable, 8)]
    public void AnEnterRefusalIsOneByteNamingItsReason(EnterRefusal reason, byte expectedByte)
    {
        byte[] expected = [.. Encoded.UInt32(PrGameSessionEnterFail.RegistryId), expectedByte];

        Assert.Equal(expected, Encoded.Bytes(new PrGameSessionEnterFail(reason).Write));
        Assert.Equal(reason, PrGameSessionEnterFail.TryRead(expected).Packet!.Reason);
    }

    [Fact]
    public void AnEnterRefusalTheClientShowsOnlyAGeneralMessageForCannotBeWritten()
    {
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrGameSessionEnterFail((EnterRefusal)9).Write));
    }

    [Fact]
    public void AQuickJoinRefusalIsItsIdAlone()
    {
        byte[] expected = Encoded.UInt32(PrGameSessionQuickJoinFail.RegistryId);

        Assert.Equal(expected, Encoded.Bytes(new PrGameSessionQuickJoinFail().Write));
        Assert.True(PrGameSessionQuickJoinFail.TryRead(expected).Succeeded);
        Assert.Equal(PacketError.TrailingBytes, PrGameSessionQuickJoinFail.TryRead([.. expected, 0x00]).Error);
    }

    [Theory]
    [InlineData(LeaveResult.Stayed, 0)]
    [InlineData(LeaveResult.Left, 1)]
    public void ALeaveAnswerIsOneByte(LeaveResult result, byte expectedByte)
    {
        byte[] expected = [.. Encoded.UInt32(PrGameSessionLeave.RegistryId), expectedByte];

        Assert.Equal(expected, Encoded.Bytes(new PrGameSessionLeave(result).Write));
        Assert.Equal(result, PrGameSessionLeave.TryRead(expected).Packet!.Result);
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrGameSessionLeave((LeaveResult)2).Write));
    }

    [Fact]
    public void ANewHostIsTheOptionAsAnObjectOfItsOwn()
    {
        var update = new PcGameSessionUpdateOption(Option);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionUpdateOption.RegistryId), .. OptionBytes];

        Assert.Equal(expected, Encoded.Bytes(update.Write));
        Assert.Equal(update, PcGameSessionUpdateOption.TryRead(expected).Packet);
    }

    [Fact]
    public void AReadyChangeIsThePlayersIdThenTheFlag()
    {
        var ready = new PcGameSessionReady(0x01020304, IsReady: true);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionReady.RegistryId), .. Encoded.UInt32(0x01020304), 0x01];

        Assert.Equal(expected, Encoded.Bytes(ready.Write));
        Assert.Equal(ready, PcGameSessionReady.TryRead(expected).Packet);
        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionReady.TryRead([.. expected[..^1], 0x02]).Error);
    }

    [Fact]
    public void ARoomListingCarriesEveryFieldInTheListsOwnOrderWithTheMapBeforeThePassword()
    {
        var list = new PcLobbyInitRoomList([GameSessionInfoPack.Listing(Room)]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcLobbyInitRoomList.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GameSessionInfoPack.RegistryId),
            .. Encoded.UInt16(0x03FF),
            .. Encoded.UInt16(0x0A0B),
            0x05,
            0x06,
            .. Encoded.Utf16("Phòng Một"),
            0x01,
            .. Encoded.UInt16(0x0003),
            0x02,
            0x07,
            0x08,
            .. Encoded.UInt16(0x0C0D),
            0x01, .. Encoded.Utf16("mật khẩu"),
        ];

        Assert.Equal(expected, Encoded.Bytes(list.Write));
        Assert.Equal(Room, Assert.Single(PcLobbyInitRoomList.TryRead(expected).Packet!.Rooms).Room);
    }

    [Fact]
    public void ARoomRemovalIsTheRemovalFlagAndTheNumberAlone()
    {
        var update = new PcLobbyUpdateRoomList([GameSessionInfoPack.Removal(0x0A0B)]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcLobbyUpdateRoomList.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GameSessionInfoPack.RegistryId), .. Encoded.UInt16(0x8000), .. Encoded.UInt16(0x0A0B),
        ];

        Assert.Equal(expected, Encoded.Bytes(update.Write));

        var read = Assert.Single(PcLobbyUpdateRoomList.TryRead(expected).Packet!.Changes);
        Assert.Equal(0x0A0B, read.RoomNumber);
        Assert.Null(read.Room);
    }

    [Fact]
    public void ARoomListUpdateAppliesItsChangesInOrder()
    {
        var update = new PcLobbyUpdateRoomList([GameSessionInfoPack.Listing(Room with { Password = null }), GameSessionInfoPack.Removal(0x0102)]);

        var read = PcLobbyUpdateRoomList.TryRead(Encoded.Bytes(update.Write));

        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(update.Changes, read.Packet.Changes);
    }

    [Fact]
    public void ARoomListEntryWithAMaskOtherThanEveryFieldOrTheRemovalIsNotRead()
    {
        byte[] partial =
        [
            .. Encoded.UInt32(PcLobbyUpdateRoomList.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GameSessionInfoPack.RegistryId), .. Encoded.UInt16(0x0008), .. Encoded.UInt16(0x0A0B), 0x07,
        ];

        Assert.Equal(PacketError.Unsupported, PcLobbyUpdateRoomList.TryRead(partial).Error);
    }

    [Fact]
    public void TheRoomListALobbyIsEnteredWithHoldsNoRemoval()
    {
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PcLobbyInitRoomList([GameSessionInfoPack.Removal(1)]).Write));

        byte[] body =
        [
            .. Encoded.UInt32(PcLobbyInitRoomList.RegistryId),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(GameSessionInfoPack.RegistryId), .. Encoded.UInt16(0x8000), .. Encoded.UInt16(0x0001),
        ];
        Assert.Equal(PacketError.UnexpectedValue, PcLobbyInitRoomList.TryRead(body).Error);
    }

    [Fact]
    public void ARoomListCountingMoreRoomsThanItHoldsIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PcLobbyInitRoomList.RegistryId), .. Encoded.UInt32(0x10000000), .. Encoded.UInt32(GameSessionInfoPack.RegistryId)];

        Assert.Equal(PacketError.CountOutOfRange, PcLobbyInitRoomList.TryRead(body).Error);
    }

    [Fact]
    public void ARoomInfoRequestIsTheRoomNumber()
    {
        var request = new PqLobbyViewRoomSimpleInfo(0x0A0B);

        byte[] expected = [.. Encoded.UInt32(PqLobbyViewRoomSimpleInfo.RegistryId), .. Encoded.UInt16(0x0A0B)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqLobbyViewRoomSimpleInfo.TryRead(expected).Packet);
    }

    [Fact]
    public void ARoomInfoAnswerIsTheResultThenEachCharactersIdNameLevelAndGender()
    {
        var player = new UserSimpleInfo(0x01020304, "Kim", 0x0506, Gender.Female);
        var answer = new PrLobbyViewRoomSimpleInfo(RoomInfoResult.Listed, [player]);

        byte[] expected =
        [
            .. Encoded.UInt32(PrLobbyViewRoomSimpleInfo.RegistryId),
            0x00,
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(UserSimpleInfo.RegistryId), .. Encoded.UInt32(0x01020304), .. Encoded.Utf16("Kim"), .. Encoded.UInt16(0x0506), 0x02,
        ];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(player, Assert.Single(PrLobbyViewRoomSimpleInfo.TryRead(expected).Packet!.Players));
    }

    [Fact]
    public void ARoomInfoAnswerForARoomThatIsGoneListsNoOne()
    {
        byte[] expected = [.. Encoded.UInt32(PrLobbyViewRoomSimpleInfo.RegistryId), 0x01, .. Encoded.UInt32(0)];

        Assert.Equal(expected, Encoded.Bytes(new PrLobbyViewRoomSimpleInfo(RoomInfoResult.Gone, []).Write));
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrLobbyViewRoomSimpleInfo((RoomInfoResult)2, []).Write));
    }

    private static byte[] UpdateOf(byte[] slot) =>
        [.. Encoded.UInt32(PcGameSessionUpdateSlot.RegistryId), 0x00, .. slot, .. Encoded.Utf16(string.Empty)];
}
