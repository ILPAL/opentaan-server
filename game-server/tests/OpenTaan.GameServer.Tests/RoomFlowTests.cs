using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;
using OpenTaan.Transport;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Creating, entering, quick-joining and leaving rooms, and what everyone in a room and in
/// a lobby is told, through real sessions on loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more
/// is on its way.
/// </remarks>
public sealed class RoomFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatingARoomSeatsTheHostFirstAndSendsItTheRoom()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await EnterLobbyAsync(host);

        // What the server decides itself is asked for otherwise here, and not taken.
        var room = await CreatedAsync(host, new GameSessionInfo(
            RoomNumber: 42,
            Status: RoomStatus.Requested,
            Grade: 7,
            Name: "Phòng một",
            GameType: GameType.TeamGame,
            GameMode: GameMode.CaptainNoItems,
            ChanceMode: ChanceMode.Bonus,
            CurrentPlayers: 5,
            MaxPlayers: GameSlotInfo.SlotCount,
            Password: null,
            MapId: 9));

        Assert.Equal(
            new GameSessionInfo(1, RoomStatus.Waiting, 0, "Phòng một", GameType.TeamGame, GameMode.CaptainNoItems, ChanceMode.Bonus, 1, 8, null, 1),
            room.Room);
        Assert.Equal(new GameSessionOption(host.PlayerId, host.PlayerId), room.Option);
        Assert.Equal(Slots([Seat(host, 0)]), room.Slots);
        Assert.Equal((byte)0, room.Difficulty);
        Assert.Empty(room.TeamOneName);
        Assert.Empty(room.TeamTwoName);
        await QuietAsync(host);
    }

    [Fact]
    public async Task TheLobbyIsToldOfANewRoomButNeverItsPassword()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(host);
        await EnterLobbyAsync(watcher, host);

        var room = await CreatedAsync(host, Requested(password: "bí mật"));

        Assert.Equal("bí mật", room.Room.Password);
        Assert.Equal(host.CharacterIds, await LeftLobbyAsync(watcher));
        Assert.Equal(room.Room with { Password = string.Empty }, await ListedAsync(watcher));
        await QuietAsync(host, watcher);
    }

    [Fact]
    public async Task AMissionRoomKeepsTheSecondTeamsSlotsForTheMission()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(host);
        await EnterLobbyAsync(watcher, host);

        var room = await CreatedAsync(host, Requested(gameType: GameType.Mission));

        Assert.Equal(
            new GameSessionInfo(1, RoomStatus.Waiting, 0, "Phòng một", GameType.Mission, GameMode.Items, ChanceMode.Basic, 1, 8, null, 0),
            room.Room);
        Assert.Equal(Slots([Seat(host, 0)], 2, 3, 6, 7), room.Slots);
        Assert.Equal((byte)0, room.Difficulty);
        Assert.Equal(host.CharacterIds, await LeftLobbyAsync(watcher));

        // The lobby's maximum is what the room can still hold: its four open slots.
        Assert.Equal(room.Room with { MaxPlayers = 4 }, await ListedAsync(watcher));
        await QuietAsync(host, watcher);
    }

    [Theory]
    [MemberData(nameof(RoomRulesTests.RefusedRequests), MemberType = typeof(RoomRulesTests))]
    public async Task ARequestTheRulesRefuseIsAnsweredAndChangesNothing(string request)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(host);
        await EnterLobbyAsync(watcher, host);

        await CreateAsync(host, RoomRulesTests.Refused[request]);

        Assert.Equal(CreateRefusal.Refused, (await host.ReceiveAsync<PrGameSessionCreateFail>(PrGameSessionCreateFail.TryRead)).Status);
        await QuietAsync(host, watcher);

        // Still in the lobby, and free to ask again.
        await CreatedAsync(host);
        Assert.Equal(host.CharacterIds, await LeftLobbyAsync(watcher));
    }

    [Fact]
    public async Task CreatingFromARoomIsRefusedAndTheRoomStaysAsItWas()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host);

        await CreateAsync(host);

        Assert.Equal(CreateRefusal.Refused, (await host.ReceiveAsync<PrGameSessionCreateFail>(PrGameSessionCreateFail.TryRead)).Status);
        var entered = await SeatedAsync(guest, room.Room.RoomNumber, host);
        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 2)]), entered.Slots);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AConnectionWithoutCharactersOrAChannelIsRefusedEveryRoomRequest()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await CreatedAsync(host);

        // Characters selected, and never moved to a channel.
        await using var wanderer = await ArriveAsync(services, channelId: 0, "Rồng");
        await RefusedEverythingAsync(wanderer);

        // Logged in, and nothing more.
        await using var connection = await SessionHarness.ConnectAsync(services, Cancel);
        await connection.Client.SendAsync(new PqUserLogin(
            [new LoginAccountInfo("solo@example.test", new Secret("digest"), 0, string.Empty)],
            new ConnectionAddress(0, 0),
            0));
        Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));
        await RefusedEverythingAsync(new Player(connection, 0, [], []));

        await QuietAsync(host);
    }

    [Fact]
    public async Task AChannelHoldsNoMoreRoomsThanItsLimit()
    {
        var services = Services(new RoomOptions { MaxPerChannel = 2 });
        await using var first = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Hoang");
        await using var elsewhere = await ArriveAsync(services, OtherChannelId, "Linh");
        await CreatedAsync(first);
        await CreatedAsync(second);

        await CreateAsync(third);

        Assert.Equal(CreateRefusal.Refused, (await third.ReceiveAsync<PrGameSessionCreateFail>(PrGameSessionCreateFail.TryRead)).Status);

        // Each channel has a limit of its own.
        Assert.Equal((ushort)3, (await CreatedAsync(elsewhere)).Room.RoomNumber);
        await QuietAsync(first, second, third, elsewhere);
    }

    [Fact]
    public async Task ARoomsNumberIsNotGivenToTheNextRoomAtOnce()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        Assert.Equal((ushort)1, (await CreatedAsync(host)).Room.RoomNumber);
        Assert.Equal(LeaveResult.Left, await LeaveAsync(host));

        Assert.Equal((ushort)2, (await CreatedAsync(host)).Room.RoomNumber);
        await QuietAsync(host);
    }

    [Fact]
    public async Task AGuestSitsOnTheTeamWithFewerAndTheRoomAndTheLobbyAreTold()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var watcher = await ArriveAsync(services, ChannelId, "Hoang");
        await EnterLobbyAsync(host);
        await EnterLobbyAsync(guest, host);
        await EnterLobbyAsync(watcher, host, guest);
        var room = await CreatedAsync(host);
        foreach (var other in new[] { guest, watcher })
        {
            Assert.Equal(host.CharacterIds, await LeftLobbyAsync(other));
            Assert.Equal(room.Room, await ListedAsync(other));
        }

        var entered = await EnteredAsync(guest, room.Room.RoomNumber);

        Assert.Equal(room.Room with { CurrentPlayers = 2 }, entered.Room);
        Assert.Equal(room.Option, entered.Option);
        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 2)]), entered.Slots);

        // Read only in a mission room; elsewhere the guest's first slot, as recorded.
        Assert.Equal((byte)2, entered.Difficulty);
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Seated, Seat(guest, 2), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(guest.CharacterIds, await LeftLobbyAsync(watcher));
        Assert.Equal(entered.Room, await ListedAsync(watcher));
        await QuietAsync(host, guest, watcher);
    }

    [Fact]
    public async Task AMissionGuestSitsOnTheFirstTeamAndIsShownTheRoomsDifficulty()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host, Requested(gameType: GameType.Mission));

        var entered = await SeatedAsync(guest, room.Room.RoomNumber, host);

        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 1)], 2, 3, 6, 7), entered.Slots);
        Assert.Equal(room.Difficulty, entered.Difficulty);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EnteringFromARoomIsRefused()
    {
        var services = Services();
        await using var first = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var guest = await ArriveAsync(services, ChannelId, "Hoang");
        var firstRoom = await CreatedAsync(first);
        var secondRoom = await CreatedAsync(second);
        await SeatedAsync(guest, firstRoom.Room.RoomNumber, first);

        Assert.Equal(EnterRefusal.AlreadyInRoom, await RefusedEntryAsync(first, firstRoom.Room.RoomNumber));
        Assert.Equal(EnterRefusal.AlreadyInRoom, await RefusedEntryAsync(first, secondRoom.Room.RoomNumber));
        Assert.Equal(EnterRefusal.AlreadyInRoom, await RefusedEntryAsync(guest, secondRoom.Room.RoomNumber));
        await QuietAsync(first, second, guest);
    }

    [Fact]
    public async Task ARoomThatIsNotThereOrIsInAnotherChannelCannotBeEntered()
    {
        var services = Services();
        await using var elsewhere = await ArriveAsync(services, OtherChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(elsewhere);

        Assert.Equal(EnterRefusal.NoSuchRoom, await RefusedEntryAsync(guest, 77));
        Assert.Equal(EnterRefusal.NoSuchRoom, await RefusedEntryAsync(guest, room.Room.RoomNumber));
        await QuietAsync(elsewhere, guest);
    }

    [Fact]
    public async Task OnlyTheRoomsPasswordLetsAPlayerIn()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host, Requested(password: "mở cửa"));

        Assert.Equal(EnterRefusal.WrongPassword, await RefusedEntryAsync(guest, room.Room.RoomNumber));
        Assert.Equal(EnterRefusal.WrongPassword, await RefusedEntryAsync(guest, room.Room.RoomNumber, "MỞ CỬA"));
        Assert.Equal(EnterRefusal.WrongPassword, await RefusedEntryAsync(guest, room.Room.RoomNumber, "mở cửa "));
        await QuietAsync(host);

        var entered = await EnteredAsync(guest, room.Room.RoomNumber, "mở cửa");
        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 2)]), entered.Slots);
        Assert.Equal("mở cửa", entered.Room.Password);
        await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AFullRoomIsRefused()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi", "Rồng");
        await using var guests = await ArriveAsync(services, ChannelId, "Hoang", "Linh");
        await using var late = await ArriveAsync(services, ChannelId, "Minh");
        var room = await CreatedAsync(host, Requested(gameType: GameType.Mission));
        await SeatedAsync(guests, room.Room.RoomNumber, host);

        Assert.Equal(EnterRefusal.RoomFull, await RefusedEntryAsync(late, room.Room.RoomNumber));
        await QuietAsync(host, guests, late);
    }

    [Fact]
    public async Task QuickJoinTakesTheFullestRoom()
    {
        var services = Services();
        await using var first = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var guest = await ArriveAsync(services, ChannelId, "Hoang");
        await using var joiner = await ArriveAsync(services, ChannelId, "Linh");
        await CreatedAsync(first);
        var fullest = await CreatedAsync(second);
        await SeatedAsync(guest, fullest.Room.RoomNumber, second);

        await QuickJoinAsync(joiner);

        var joined = await joiner.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead);
        Assert.Equal(fullest.Room.RoomNumber, joined.Room.RoomNumber);
        Assert.Equal(Slots([Seat(second, 0), Seat(joiner, 1), Seat(guest, 2)]), joined.Slots);
        foreach (var member in new[] { second, guest })
        {
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Seated, Seat(joiner, 1), string.Empty),
                await member.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        await QuietAsync(first, second, guest, joiner);
    }

    [Fact]
    public async Task QuickJoinTakesTheLowestNumberOfRoomsEquallyFull()
    {
        var services = Services();
        await using var first = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Hoang");
        await using var joiner = await ArriveAsync(services, ChannelId, "Linh");
        await CreatedAsync(second);
        var lowest = await CreatedAsync(first);
        Assert.Equal(LeaveResult.Left, await LeaveAsync(second));
        await CreatedAsync(second);
        await CreatedAsync(third);

        await QuickJoinAsync(joiner);

        Assert.Equal(lowest.Room.RoomNumber, (await joiner.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead)).Room.RoomNumber);
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Seated, Seat(joiner, 2), string.Empty),
            await first.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await QuietAsync(first, second, third, joiner);
    }

    [Fact]
    public async Task QuickJoinPassesOverRoomsWithAPasswordAMissionOrAnotherChannel()
    {
        var services = Services();
        await using var locked = await ArriveAsync(services, ChannelId, "Kimi");
        await using var mission = await ArriveAsync(services, ChannelId, "Rồng");
        await using var elsewhere = await ArriveAsync(services, OtherChannelId, "Hoang");
        await using var joiner = await ArriveAsync(services, ChannelId, "Linh");
        await CreatedAsync(locked, Requested(password: "khóa"));
        await CreatedAsync(mission, Requested(gameType: GameType.Mission));
        await CreatedAsync(elsewhere);

        await QuickJoinAsync(joiner);

        await joiner.ReceiveAsync<PrGameSessionQuickJoinFail>(PrGameSessionQuickJoinFail.TryRead);
        await QuietAsync(locked, mission, elsewhere, joiner);

        await using var open = await ArriveAsync(services, ChannelId, "Minh");
        var room = await CreatedAsync(open);
        await QuickJoinAsync(joiner);
        Assert.Equal(room.Room.RoomNumber, (await joiner.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead)).Room.RoomNumber);
        await open.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        await QuietAsync(open, joiner);
    }

    [Fact]
    public async Task QuickJoiningFromARoomIsRefused()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var other = await ArriveAsync(services, ChannelId, "Rồng");
        await CreatedAsync(host);
        await CreatedAsync(other);

        await QuickJoinAsync(host);

        await host.ReceiveAsync<PrGameSessionQuickJoinFail>(PrGameSessionQuickJoinFail.TryRead);
        await QuietAsync(host, other);
    }

    [Fact]
    public async Task ALeaveIsAnsweredAndEveryoneLeftIsToldOfTheFreedSlot()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var watcher = await ArriveAsync(services, ChannelId, "Hoang");
        await EnterLobbyAsync(watcher);
        var room = await CreatedAsync(host);
        await ListedAsync(watcher);
        await SeatedAsync(guest, room.Room.RoomNumber, host);
        await ListedAsync(watcher);

        // The number a leave names is not the server's to go by.
        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest, roomNumber: 0));

        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(room.Room, await ListedAsync(watcher));
        await QuietAsync(host, guest, watcher);
    }

    [Fact]
    public async Task WhenTheHostLeavesTheLowestSeatHostsAndIsFirstToldItIsNotReady()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var first = await ArriveAsync(services, ChannelId, "Rồng");
        await using var second = await ArriveAsync(services, ChannelId, "Hoang");
        var room = await CreatedAsync(host);
        await SeatedAsync(first, room.Room.RoomNumber, host);
        var entered = await SeatedAsync(second, room.Room.RoomNumber, host, first);
        Assert.Equal(Slots([Seat(host, 0), Seat(second, 1), Seat(first, 2)]), entered.Slots);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(host));

        foreach (var left in new[] { first, second })
        {
            await HandedOverAsync(left, freedSlot: 0, newHost: second);
        }

        await QuietAsync(host, first, second);
    }

    [Fact]
    public async Task TheLastPlayerLeavingClosesTheRoom()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(watcher);
        var room = await CreatedAsync(host);
        await ListedAsync(watcher);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(host));

        Assert.Equal(room.Room.RoomNumber, await RemovedAsync(watcher));
        await watcher.SendAsync(new PqLobbyViewRoomSimpleInfo(room.Room.RoomNumber));
        Assert.Equal(RoomInfoResult.Gone, (await watcher.ReceiveAsync<PrLobbyViewRoomSimpleInfo>(PrLobbyViewRoomSimpleInfo.TryRead)).Result);
        await QuietAsync(host, watcher);
    }

    [Fact]
    public async Task ALeaveOutsideARoomIsStillAnsweredAsLeft()
    {
        var services = Services();
        await using var player = await ArriveAsync(services, ChannelId, "Kimi");
        await EnterLobbyAsync(player);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(player, roomNumber: 5));
        await QuietAsync(player);
    }

    [Fact]
    public async Task AConnectionThatClosesLeavesItsRoomAndTheRoomIsTold()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var last = await ArriveAsync(services, ChannelId, "Hoang");
        await using var watcher = await ArriveAsync(services, ChannelId, "Linh");
        await EnterLobbyAsync(watcher);
        var room = await CreatedAsync(host);
        await ListedAsync(watcher);
        await SeatedAsync(guest, room.Room.RoomNumber, host);
        await SeatedAsync(last, room.Room.RoomNumber, host, guest);
        await ListedAsync(watcher);
        await ListedAsync(watcher);

        await guest.DisposeAsync();
        await guest.Connection.ClosedReason();

        foreach (var member in new[] { host, last })
        {
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty),
                await member.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        Assert.Equal((byte)2, (await ListedAsync(watcher)).CurrentPlayers);

        // A host that closes hands the room on.
        await host.DisposeAsync();
        await host.Connection.ClosedReason();

        await HandedOverAsync(last, freedSlot: 0, newHost: last);
        Assert.Equal((byte)1, (await ListedAsync(watcher)).CurrentPlayers);
        await QuietAsync(last, watcher);
    }

    [Fact]
    public async Task LeavingTheLobbyOnceInARoomKeepsTheSeat()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(host);
        var room = await CreatedAsync(host);

        // As the client does once it has the room it entered.
        await host.SendAsync(new PqLobbyLeave());
        await host.ReceiveAsync<PrLobbyLeave>(PrLobbyLeave.TryRead);

        var entered = await SeatedAsync(guest, room.Room.RoomNumber, host);
        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 2)]), entered.Slots);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EnteringTheLobbyStillInARoomLeavesTheRoomFirst()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host);
        await SeatedAsync(guest, room.Room.RoomNumber, host);

        var listed = await EnterLobbyAsync(guest);

        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(room.Room, Assert.Single(listed).Room);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task MovingToAnotherChannelFromARoomLeavesTheRoom()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host);
        await SeatedAsync(guest, room.Room.RoomNumber, host);

        Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(guest, OtherChannelId));

        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheViewOfARoomListsItsCharactersInSlotOrder()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var first = await ArriveAsync(services, ChannelId, "Rồng");
        await using var second = await ArriveAsync(services, ChannelId, "Hoang");
        await using var watcher = await ArriveAsync(services, ChannelId, "Linh");
        var room = await CreatedAsync(host);
        await SeatedAsync(first, room.Room.RoomNumber, host);
        await SeatedAsync(second, room.Room.RoomNumber, host, first);

        await watcher.SendAsync(new PqLobbyViewRoomSimpleInfo(room.Room.RoomNumber));
        var view = await watcher.ReceiveAsync<PrLobbyViewRoomSimpleInfo>(PrLobbyViewRoomSimpleInfo.TryRead);

        Assert.Equal(RoomInfoResult.Listed, view.Result);
        Assert.Equal(
            new[] { host, second, first }.Select(player => new UserSimpleInfo(
                player.Characters[0].CharacterId,
                player.Characters[0].Name,
                player.Characters[0].Level,
                player.Characters[0].Gender)),
            view.Players);
        await QuietAsync(host, first, second, watcher);
    }

    [Fact]
    public async Task TheViewOfARoomThatIsGoneOrInAnotherChannelSaysItIsGone()
    {
        var services = Services();
        await using var elsewhere = await ArriveAsync(services, OtherChannelId, "Kimi");
        await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(elsewhere);

        foreach (var roomNumber in new[] { (ushort)77, room.Room.RoomNumber })
        {
            await watcher.SendAsync(new PqLobbyViewRoomSimpleInfo(roomNumber));
            var view = await watcher.ReceiveAsync<PrLobbyViewRoomSimpleInfo>(PrLobbyViewRoomSimpleInfo.TryRead);
            Assert.Equal(RoomInfoResult.Gone, view.Result);
            Assert.Empty(view.Players);
        }

        await QuietAsync(elsewhere, watcher);
    }

    [Fact]
    public async Task ALobbyIsEnteredWithItsChannelsRoomsListedWithoutTheirPasswords()
    {
        var services = Services();
        await using var locked = await ArriveAsync(services, ChannelId, "Kimi");
        await using var mission = await ArriveAsync(services, ChannelId, "Rồng");
        await using var elsewhere = await ArriveAsync(services, OtherChannelId, "Hoang");
        await using var watcher = await ArriveAsync(services, ChannelId, "Linh");
        var lockedRoom = await CreatedAsync(locked, Requested(name: "Có khóa", password: "khóa"));
        var missionRoom = await CreatedAsync(mission, Requested(name: "Nhiệm vụ", gameType: GameType.Mission));
        await CreatedAsync(elsewhere);

        var listed = await EnterLobbyAsync(watcher);

        Assert.Equal(
            [lockedRoom.Room with { Password = string.Empty }, missionRoom.Room with { MaxPlayers = 4 }],
            listed.Select(entry => entry.Room));
        await QuietAsync(locked, mission, elsewhere, watcher);
    }

    [Fact]
    public void ARoomListTooLargeForOneFrameIsListedThenAddedInFramesThatFit()
    {
        var listings = Enumerable.Range(1, 500)
            .Select(number => GameSessionInfoPack.Listing(Requested(name: new string('P', 128)) with { RoomNumber = (ushort)number }))
            .ToArray();

        var packets = RoomDirectory.ListOf(listings);

        Assert.IsType<PcLobbyInitRoomList>(packets[0]);
        Assert.True(packets.Count > 1);
        Assert.All(packets.Skip(1), packet => Assert.IsType<PcLobbyUpdateRoomList>(packet));
        Assert.All(packets, packet => Assert.InRange(Encoded.Bytes(packet.Write).Length, 1, TransportLimits.MaxEncryptedBody));
        Assert.Equal(listings, packets.SelectMany(packet => packet switch
        {
            PcLobbyInitRoomList list => list.Rooms,
            PcLobbyUpdateRoomList update => update.Changes,
            _ => [],
        }));
    }

    [Fact]
    public void ARoomListThatFitsOneFrameIsOneList()
    {
        GameSessionInfoPack[] listings = [GameSessionInfoPack.Listing(Requested() with { RoomNumber = 3 })];

        Assert.Equal(listings, Assert.IsType<PcLobbyInitRoomList>(Assert.Single(RoomDirectory.ListOf(listings))).Rooms);
        Assert.Empty(Assert.IsType<PcLobbyInitRoomList>(Assert.Single(RoomDirectory.ListOf([]))).Rooms);
    }

    // A refusal for every room request, from a connection that cannot be in a room.
    private static async Task RefusedEverythingAsync(Player player)
    {
        await CreateAsync(player);
        Assert.Equal(CreateRefusal.Refused, (await player.ReceiveAsync<PrGameSessionCreateFail>(PrGameSessionCreateFail.TryRead)).Status);
        Assert.Equal(EnterRefusal.NoSuchRoom, await RefusedEntryAsync(player, 1));
        await QuickJoinAsync(player);
        await player.ReceiveAsync<PrGameSessionQuickJoinFail>(PrGameSessionQuickJoinFail.TryRead);
        Assert.Equal(LeaveResult.Left, await LeaveAsync(player));
        await player.SendAsync(new PqLobbyViewRoomSimpleInfo(1));
        Assert.Equal(RoomInfoResult.Gone, (await player.ReceiveAsync<PrLobbyViewRoomSimpleInfo>(PrLobbyViewRoomSimpleInfo.TryRead)).Result);
        await QuietAsync(player);
    }

    // A member is told, in order: the host's slot freed, the new host not ready, then who hosts.
    private static async Task HandedOverAsync(Player member, byte freedSlot, Player newHost)
    {
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(freedSlot), string.Empty),
            await member.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(new PcGameSessionReady(newHost.PlayerId, IsReady: false), await member.ReceiveAsync<PcGameSessionReady>(PcGameSessionReady.TryRead));
        Assert.Equal(
            new GameSessionOption(newHost.PlayerId, newHost.PlayerId),
            (await member.ReceiveAsync<PcGameSessionUpdateOption>(PcGameSessionUpdateOption.TryRead)).Option);
    }

    // What the next change to a lobby's player list removes. It adds no one.
    private static async Task<IReadOnlyList<uint>> LeftLobbyAsync(Player player)
    {
        var update = await player.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
        Assert.Empty(update.Characters);
        return update.RemovedCharacterIds;
    }

    // The room the next change to a lobby's room list lists, alone.
    private static async Task<GameSessionInfo> ListedAsync(Player player)
    {
        var change = Assert.Single((await player.ReceiveAsync<PcLobbyUpdateRoomList>(PcLobbyUpdateRoomList.TryRead)).Changes);
        return Assert.IsType<GameSessionInfo>(change.Room);
    }

    // The room the next change to a lobby's room list removes, alone.
    private static async Task<ushort> RemovedAsync(Player player)
    {
        var change = Assert.Single((await player.ReceiveAsync<PcLobbyUpdateRoomList>(PcLobbyUpdateRoomList.TryRead)).Changes);
        Assert.Null(change.Room);
        return change.RoomNumber;
    }
}
