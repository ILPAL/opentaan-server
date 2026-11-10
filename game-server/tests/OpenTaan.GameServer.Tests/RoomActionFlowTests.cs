using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// What players do in a room: being ready, changing team and weapon, the host's options,
/// slots and expulsions, through real sessions on loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. A request the room's rules do not allow is dropped, so for one of those,
/// that nothing is sent is what is checked.
/// </remarks>
public sealed class RoomActionFlowTests
{
    /// <summary>A host's request each options rule refuses, with the kind of room it is made in, by what is wrong with it.</summary>
    internal static readonly IReadOnlyDictionary<string, (GameType Room, PqGameSessionChangeGameOption Request)> RefusedOptions =
        new Dictionary<string, (GameType, PqGameSessionChangeGameOption)>
        {
            ["no map"] = (GameType.TeamGame, new(0, GameMode.Items, ChanceMode.Bonus, 0xFF)),
            ["a map past the client's list"] = (GameType.TeamGame, new(22, GameMode.Items, ChanceMode.Bonus, 0xFF)),
            ["a map the client has and never lists"] = (GameType.TeamGame, new(300, GameMode.Items, ChanceMode.Bonus, 0xFF)),
            ["another map the client has and never lists"] = (GameType.TeamGame, new(500, GameMode.Items, ChanceMode.Bonus, 0xFF)),
            ["a game mode that does not exist"] = (GameType.TeamGame, new(1, (GameMode)4, ChanceMode.Bonus, 0xFF)),
            ["a chance mode that does not exist"] = (GameType.TeamGame, new(1, GameMode.Items, (ChanceMode)3, 0xFF)),
            ["a captain mode in a mission room"] = (GameType.Mission, new(0, GameMode.CaptainItems, ChanceMode.Bonus, 0)),
            ["a difficulty past the hardest"] = (GameType.Mission, new(0, GameMode.Items, ChanceMode.Bonus, 3)),
        };

    public static TheoryData<string> RefusedOptionRequests => [.. RefusedOptions.Keys];

    [Fact]
    public async Task AGuestReadyingAndNoLongerReadyIsToldToEveryoneItselfIncluded()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await EveryoneToldAsync(new PcGameSessionReady(guest.PlayerId, IsReady: true), PcGameSessionReady.TryRead, host, guest);

        await guest.SendAsync(new PqGameSessionReady(IsReady: false));
        await EveryoneToldAsync(new PcGameSessionReady(guest.PlayerId, IsReady: false), PcGameSessionReady.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task APlayerEnteringLaterIsShownWhoIsReady()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);
        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await EveryoneToldAsync(new PcGameSessionReady(guest.PlayerId, IsReady: true), PcGameSessionReady.TryRead, host, guest);

        var entered = await SeatedAsync(late, number, host, guest);

        Assert.Equal(Slots([Seat(host, 0), Seat(late, 1), Seat(guest, 2, ready: true)]), entered.Slots);
        await QuietAsync(host, guest, late);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionAreReadyTogetherInOneChange()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(pair, number, host);

        await pair.SendAsync(new PqGameSessionReady(IsReady: true));

        await EveryoneToldAsync(new PcGameSessionReady(pair.PlayerId, IsReady: true), PcGameSessionReady.TryRead, host, pair);
        await QuietAsync(host, pair);
        var entered = await SeatedAsync(late, number, host, pair);
        Assert.Equal(Slots([Seat(host, 0), Seat(late, 1), Seat(pair, 2, 0, ready: true), Seat(pair, 3, 1, ready: true)]), entered.Slots);
    }

    [Fact]
    public async Task TheHostCannotBeReadySinceItStartsTheMatch()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await host.SendAsync(new PqGameSessionReady(IsReady: true));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGuestChangingTeamTakesTheLowestOpenSlotThereAndEveryoneIsTold()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionChangeTeam(1));

        await EveryoneToldAsync(
            new PcGameSessionChangeTeam(guest.PlayerId, 1, 2, PcGameSessionChangeTeam.NoSlot, 1, PcGameSessionChangeTeam.NoSlot),
            PcGameSessionChangeTeam.TryRead,
            host,
            guest);
        await QuietAsync(host, guest);

        // The first team now holds two, so a player entering sits on the second.
        var entered = await SeatedAsync(late, number, host, guest);
        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 1), Seat(late, 2)]), entered.Slots);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionChangeTeamTogetherAndKeepTheirWeapons()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(pair, number, host);
        await pair.SendAsync(new PqGameSessionChangeWeapon(pair.CharacterIds[1], 0x0102));
        await EveryoneToldAsync(new PcGameSessionChangeWeapon(pair.CharacterIds[1], 0x0102), PcGameSessionChangeWeapon.TryRead, host, pair);

        await pair.SendAsync(new PqGameSessionChangeTeam(1));

        // From slots 2 and 3 to the first team's lowest open ones, 1 and 4, in order.
        await EveryoneToldAsync(new PcGameSessionChangeTeam(pair.PlayerId, 1, 2, 3, 1, 4), PcGameSessionChangeTeam.TryRead, host, pair);
        await QuietAsync(host, pair);
        var entered = await SeatedAsync(late, number, host, pair);
        Assert.Equal(Slots([Seat(host, 0), Seat(pair, 1, 0), Seat(late, 2), Seat(pair, 4, 1, weaponId: 0x0102)]), entered.Slots);
    }

    [Fact]
    public async Task ATeamChangeToNoOtherTeamOrFromAReadyPlayerIsDropped()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        // The team the guest is on, and teams there are not.
        foreach (var team in new byte[] { 2, 0, 3, 0xFF })
        {
            await guest.SendAsync(new PqGameSessionChangeTeam(team));
        }

        await QuietAsync(host, guest);

        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await EveryoneToldAsync(new PcGameSessionReady(guest.PlayerId, IsReady: true), PcGameSessionReady.TryRead, host, guest);
        await guest.SendAsync(new PqGameSessionChangeTeam(1));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ATeamChangeInAMissionRoomIsDropped()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host, Requested(gameType: GameType.Mission))).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionChangeTeam(2));
        await host.SendAsync(new PqGameSessionChangeTeam(2));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ATeamChangeIsDroppedWhenTheOtherTeamCannotSeatEveryCharacterOfTheConnection()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(pair, number, host);
        foreach (var slot in new byte[] { 4, 5 })
        {
            await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, slot));
            await EveryoneToldAsync(new PcGameSessionOpenCloseSlot(Open: false, slot), PcGameSessionOpenCloseSlot.TryRead, host, pair);
        }

        // The first team has one open slot left, for two characters.
        await pair.SendAsync(new PqGameSessionChangeTeam(1));

        await QuietAsync(host, pair);
    }

    [Fact]
    public async Task AWeaponForOnesOwnCharacterIsToldToEveryoneAndKeptForPlayersEnteringLater()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionChangeWeapon(guest.CharacterIds[0], 0x0102));
        await EveryoneToldAsync(new PcGameSessionChangeWeapon(guest.CharacterIds[0], 0x0102), PcGameSessionChangeWeapon.TryRead, host, guest);

        // The client's random pick is kept as it is: what it becomes is the match's to decide.
        await host.SendAsync(new PqGameSessionChangeWeapon(host.CharacterIds[0], 200));
        await EveryoneToldAsync(new PcGameSessionChangeWeapon(host.CharacterIds[0], 200), PcGameSessionChangeWeapon.TryRead, host, guest);
        await QuietAsync(host, guest);

        var entered = await SeatedAsync(late, number, host, guest);
        Assert.Equal(Slots([Seat(host, 0, weaponId: 200), Seat(late, 1), Seat(guest, 2, weaponId: 0x0102)]), entered.Slots);
    }

    [Fact]
    public async Task AWeaponForACharacterThatIsNotOnesOwnIsDropped()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionChangeWeapon(host.CharacterIds[0], 0x0102));
        await guest.SendAsync(new PqGameSessionChangeWeapon(late.CharacterIds[0], 0x0102));

        await QuietAsync(host, guest);
        var entered = await SeatedAsync(late, number, host, guest);
        Assert.Equal(Slots([Seat(host, 0), Seat(late, 1), Seat(guest, 2)]), entered.Slots);
    }

    [Fact]
    public async Task TheHostsNewOptionsReachEveryoneInTheRoomAndTheLobby()
    {
        var services = Services();
        await using var watcher = await ArriveAsync(services, ChannelId, "Minh");
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await EnterLobbyAsync(watcher);
        var room = await CreatedAsync(host);
        await ListedAsync(watcher);
        await SeatedAsync(guest, room.Room.RoomNumber, host);
        await ListedAsync(watcher);

        await host.SendAsync(new PqGameSessionChangeGameOption(7, GameMode.CaptainNoItems, ChanceMode.Basic, 0xFF));

        await EveryoneToldAsync(new PcGameSessionChangeGameOption(7, GameMode.CaptainNoItems, ChanceMode.Basic, 0xFF), PcGameSessionChangeGameOption.TryRead, host, guest);
        Assert.Equal(
            room.Room with { GameMode = GameMode.CaptainNoItems, ChanceMode = ChanceMode.Basic, MapId = 7, CurrentPlayers = 2 },
            await ListedAsync(watcher));
        await QuietAsync(host, guest, watcher);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(203)]
    public async Task EveryMapTheClientListsIsKeptAndShownToPlayersEnteringLaterItsRandomPicksIncluded(ushort map)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var room = await CreatedAsync(host);
        await SeatedAsync(guest, room.Room.RoomNumber, host);

        await host.SendAsync(new PqGameSessionChangeGameOption(map, GameMode.Items, ChanceMode.Bonus, 0xFF));

        await EveryoneToldAsync(new PcGameSessionChangeGameOption(map, GameMode.Items, ChanceMode.Bonus, 0xFF), PcGameSessionChangeGameOption.TryRead, host, guest);
        await QuietAsync(host, guest);
        var entered = await SeatedAsync(late, room.Room.RoomNumber, host, guest);
        Assert.Equal(room.Room with { ChanceMode = ChanceMode.Bonus, MapId = map, CurrentPlayers = 3 }, entered.Room);
    }

    [Theory]
    [MemberData(nameof(RefusedOptionRequests))]
    public async Task ARefusedOptionsRequestIsAnsweredWithTheRoomAsItIsToTheHostAlone(string request)
    {
        var (gameType, refused) = RefusedOptions[request];
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await CreatedAsync(host, Requested(gameType: gameType));
        await SeatedAsync(guest, room.Room.RoomNumber, host);

        await host.SendAsync(refused);

        Assert.Equal(
            new PcGameSessionChangeGameOption(room.Room.MapId, GameMode.Items, ChanceMode.Basic, 0),
            await host.ReceiveAsync<PcGameSessionChangeGameOption>(PcGameSessionChangeGameOption.TryRead));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AnOptionsRequestFromAGuestIsDropped()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionChangeGameOption(7, GameMode.Items, ChanceMode.Bonus, 0xFF));

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task OutsideAMissionTheDifficultyIsSentBackAsItCameAndNotKept()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        // The host's difficulty buttons, which it has in every room, send one less than the dialog's 0xFF.
        await host.SendAsync(new PqGameSessionChangeGameOption(1, GameMode.Items, ChanceMode.Basic, 0xFE));
        await EveryoneToldAsync(new PcGameSessionChangeGameOption(1, GameMode.Items, ChanceMode.Basic, 0xFE), PcGameSessionChangeGameOption.TryRead, host, guest);

        // A refusal sends the room back as it is, and it kept none of that.
        await host.SendAsync(new PqGameSessionChangeGameOption(0, GameMode.Items, ChanceMode.Basic, 0xFE));
        Assert.Equal(
            new PcGameSessionChangeGameOption(1, GameMode.Items, ChanceMode.Basic, 0),
            await host.ReceiveAsync<PcGameSessionChangeGameOption>(PcGameSessionChangeGameOption.TryRead));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMissionTheHostPicksAndItsDifficultyAreKeptAndShownToPlayersEnteringLater()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host, Requested(gameType: GameType.Mission))).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        // A mission's number, which no list of maps holds.
        await host.SendAsync(new PqGameSessionChangeGameOption(0x0BB8, GameMode.Items, ChanceMode.Basic, RoomRules.HardestDifficulty));

        await EveryoneToldAsync(new PcGameSessionChangeGameOption(0x0BB8, GameMode.Items, ChanceMode.Basic, RoomRules.HardestDifficulty), PcGameSessionChangeGameOption.TryRead, host, guest);
        await QuietAsync(host, guest);
        var entered = await SeatedAsync(late, number, host, guest);
        Assert.Equal((ushort)0x0BB8, entered.Room.MapId);
        Assert.Equal(RoomRules.HardestDifficulty, entered.Difficulty);
    }

    [Fact]
    public async Task ClosingAndOpeningASlotIsToldToTheRoomAndChangesTheLobbysMaximum()
    {
        var services = Services();
        await using var watcher = await ArriveAsync(services, ChannelId, "Minh");
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        await EnterLobbyAsync(watcher);
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await ListedAsync(watcher);
        await SeatedAsync(guest, number, host);
        await ListedAsync(watcher);

        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 7));

        await EveryoneToldAsync(new PcGameSessionOpenCloseSlot(Open: false, 7), PcGameSessionOpenCloseSlot.TryRead, host, guest);
        Assert.Equal((byte)7, (await ListedAsync(watcher)).MaxPlayers);
        await QuietAsync(host, guest, watcher);

        // A player entering meanwhile is shown the slot closed.
        var entered = await SeatedAsync(late, number, host, guest);
        Assert.Equal(Slots([Seat(host, 0), Seat(late, 1), Seat(guest, 2)], 7), entered.Slots);
        await ListedAsync(watcher);

        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: true, 7));

        await EveryoneToldAsync(new PcGameSessionOpenCloseSlot(Open: true, 7), PcGameSessionOpenCloseSlot.TryRead, host, guest, late);
        Assert.Equal((byte)8, (await ListedAsync(watcher)).MaxPlayers);
        await QuietAsync(host, guest, late, watcher);
    }

    [Fact]
    public async Task OnlyTheHostChangesASlotAndOnlyAnEmptyOneThatWouldChange()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        // Slots holding a character, the host's own one too; a slot the room does not have;
        // a slot open already; and a guest asking.
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 2));
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 0));
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, GameSlotInfo.SlotCount));
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: true, 5));
        await guest.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 5));
        await QuietAsync(host, guest);

        // A slot closed already.
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 5));
        await EveryoneToldAsync(new PcGameSessionOpenCloseSlot(Open: false, 5), PcGameSessionOpenCloseSlot.TryRead, host, guest);
        await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 5));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMissionRoomsOwnSideStaysClosed()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await CreatedAsync(host, Requested(gameType: GameType.Mission));

        foreach (var slot in new byte[] { 2, 3, 6, 7 })
        {
            await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: true, slot));
        }

        await QuietAsync(host);
    }

    [Fact]
    public async Task ClosedSlotsArePassedOverByPlayersEnteringAndQuickJoining()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var first = await ArriveAsync(services, ChannelId, "Rồng");
        await using var second = await ArriveAsync(services, ChannelId, "Linh");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        foreach (var slot in new byte[] { 1, 2, 3, 4, 5, 6 })
        {
            await host.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, slot));
            await EveryoneToldAsync(new PcGameSessionOpenCloseSlot(Open: false, slot), PcGameSessionOpenCloseSlot.TryRead, host);
        }

        // The one slot left open is the one a player entering takes, and then the room is full.
        var entered = await SeatedAsync(first, number, host);
        Assert.Equal(Slots([Seat(host, 0), Seat(first, 7)], 1, 2, 3, 4, 5, 6), entered.Slots);
        Assert.Equal(EnterRefusal.RoomFull, await RefusedEntryAsync(second, number));
        await QuickJoinAsync(second);
        await second.ReceiveAsync<PrGameSessionQuickJoinFail>(PrGameSessionQuickJoinFail.TryRead);
        await QuietAsync(host, first, second);
    }

    [Fact]
    public async Task AnExpulsionNamesThePlayerToEveryoneThenThePlayerLeavesAndIsAnsweredAsLeft()
    {
        var services = Services();
        await using var watcher = await ArriveAsync(services, ChannelId, "Minh");
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var other = await ArriveAsync(services, ChannelId, "Linh");
        await EnterLobbyAsync(watcher);
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await ListedAsync(watcher);
        await SeatedAsync(guest, number, host);
        await ListedAsync(watcher);
        await SeatedAsync(other, number, host, guest);
        await ListedAsync(watcher);

        await host.SendAsync(new PqGameSessionUserExpel(guest.CharacterIds[0]));

        // Its player's id, not the character's, is what each client finds the slots by.
        await EveryoneToldAsync(new PcGameSessionUserExpel(guest.PlayerId), PcGameSessionUserExpel.TryRead, host, other, guest);
        await EveryoneToldAsync(new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty), PcGameSessionUpdateSlot.TryRead, host, other);
        Assert.Equal((byte)2, (await ListedAsync(watcher)).CurrentPlayers);
        Assert.Null(services.Rooms.RoomOf(guest.Connection.Session));

        // Its client asks to leave as soon as it is told.
        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest, number));
        await QuietAsync(host, other, guest, watcher);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionAreSentOutTogether()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(pair, number, host);

        await host.SendAsync(new PqGameSessionUserExpel(pair.CharacterIds[1]));

        await EveryoneToldAsync(new PcGameSessionUserExpel(pair.PlayerId), PcGameSessionUserExpel.TryRead, host, pair);
        foreach (var slot in new byte[] { 2, 3 })
        {
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(slot), string.Empty),
                await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        Assert.Equal(LeaveResult.Left, await LeaveAsync(pair, number));
        await QuietAsync(host, pair);
    }

    [Fact]
    public async Task AnExpulsionFromAGuestOrOfNoCharacterOrOfTheHostsOwnIsDropped()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi", "Hoang");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);

        await guest.SendAsync(new PqGameSessionUserExpel(host.CharacterIds[0]));
        await host.SendAsync(new PqGameSessionUserExpel(PqGameSessionUserExpel.NoCharacter));
        await host.SendAsync(new PqGameSessionUserExpel(0x7FFF0000));
        await host.SendAsync(new PqGameSessionUserExpel(host.CharacterIds[1]));

        await QuietAsync(host, guest);
        Assert.Equal(number, services.Rooms.RoomOf(guest.Connection.Session));
    }

    [Fact]
    public async Task APlayerSentOutMayComeBack()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var number = (await CreatedAsync(host)).Room.RoomNumber;
        await SeatedAsync(guest, number, host);
        await host.SendAsync(new PqGameSessionUserExpel(guest.CharacterIds[0]));
        await EveryoneToldAsync(new PcGameSessionUserExpel(guest.PlayerId), PcGameSessionUserExpel.TryRead, host, guest);
        await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest, number));

        var back = await SeatedAsync(guest, number, host);

        Assert.Equal(Slots([Seat(host, 0), Seat(guest, 2)]), back.Slots);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ActingOnARoomFromOutsideOneChangesNothing()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var outsider = await ArriveAsync(services, ChannelId, "Rồng");
        await CreatedAsync(host);

        await outsider.SendAsync(new PqGameSessionReady(IsReady: true));
        await outsider.SendAsync(new PqGameSessionChangeTeam(1));
        await outsider.SendAsync(new PqGameSessionChangeWeapon(outsider.CharacterIds[0], 0x0102));
        await outsider.SendAsync(new PqGameSessionChangeGameOption(7, GameMode.Items, ChanceMode.Bonus, 0xFF));
        await outsider.SendAsync(new PqGameSessionOpenCloseSlot(Open: false, 5));
        await outsider.SendAsync(new PqGameSessionUserExpel(host.CharacterIds[0]));

        await QuietAsync(host, outsider);
    }

    // Each player reads the same packet next.
    private static async Task EveryoneToldAsync<TPacket>(TPacket expected, PacketDecoder<TPacket> decode, params Player[] players)
        where TPacket : class
    {
        foreach (var player in players)
        {
            Assert.Equal(expected, await player.ReceiveAsync(decode));
        }
    }

    // The room the next change to a lobby's room list lists, alone.
    private static async Task<GameSessionInfo> ListedAsync(Player player)
    {
        var change = Assert.Single((await player.ReceiveAsync<PcLobbyUpdateRoomList>(PcLobbyUpdateRoomList.TryRead)).Changes);
        return Assert.IsType<GameSessionInfo>(change.Room);
    }
}
