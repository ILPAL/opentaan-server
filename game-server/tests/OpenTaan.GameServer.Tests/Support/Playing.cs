using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>A connection that has logged in and selected its characters, one per account.</summary>
/// <param name="Connection">The connection.</param>
/// <param name="PlayerId">The id its login was answered with.</param>
/// <param name="Characters">Its characters, in the order of its accounts.</param>
/// <param name="Appearances">How each of them looks, in the same order.</param>
internal sealed record Player(
    Connection Connection,
    uint PlayerId,
    IReadOnlyList<CharacterInfo> Characters,
    IReadOnlyList<CharacterAppear> Appearances) : IAsyncDisposable
{
    /// <summary>The client's end of the connection.</summary>
    public LoopbackClient Client => Connection.Client;

    /// <summary>The ids of its characters, in the order of its accounts.</summary>
    public IReadOnlyList<uint> CharacterIds => [.. Characters.Select(character => character.CharacterId)];

    /// <summary>Reads the next packet, failing the test if it is not one of these.</summary>
    public Task<TPacket> ReceiveAsync<TPacket>(PacketDecoder<TPacket> decode)
        where TPacket : class => Client.ReceiveAsync(decode);

    /// <summary>Sends a packet.</summary>
    public Task SendAsync(IPacket packet) => Client.SendAsync(packet);

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}

/// <summary>Brings players to a channel's lobby and into its rooms, as the client does. Every value is invented.</summary>
internal static class Playing
{
    /// <summary>The channel players are brought to.</summary>
    public const ushort ChannelId = 11;

    /// <summary>A second channel, whose lobby and rooms the first one's players do not see.</summary>
    public const ushort OtherChannelId = 12;

    /// <summary>The report on its own code each room request carries, which the server does not check.</summary>
    public static readonly IntegrityReport Report = new(0x01020304, 0x05060708, 0x09, 0x0A0B0C0D, 0x0E0F1011, 0x12131415);

    private static int _accounts;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>The two channels.</summary>
    public static ChannelOptions Channels() => new()
    {
        Server = new ChannelServer { Name = "Gamma", Type = 4, PublicAddress = "203.0.113.7", PublicPort = 2190 },
        List =
        [
            new ChannelSettings { Id = ChannelId, Name = "Kênh A", Capacity = 50 },
            new ChannelSettings { Id = OtherChannelId, Name = "Kênh B", Capacity = 50 },
        ],
    };

    /// <summary>What sessions share, with any login creating its account.</summary>
    public static SessionServices Services(RoomOptions? rooms = null, GameServerOptions? options = null) =>
        SessionHarness.Services(
            requireRegistration: false,
            store: new InMemoryAccountStore(0),
            options: options,
            channelOptions: Channels(),
            roomOptions: rooms);

    /// <summary>
    /// Connects, logs in with one new account for each name, creates a character with that
    /// name on each, selects them, and moves to <paramref name="channelId"/> unless it is 0.
    /// </summary>
    public static async Task<Player> ArriveAsync(SessionServices services, ushort channelId, params string[] names) =>
        await ArriveAsync(await SessionHarness.ConnectAsync(services, Cancel), channelId, names);

    /// <summary>As <see cref="ArriveAsync(SessionServices, ushort, string[])"/>, on a connection already open.</summary>
    public static async Task<Player> ArriveAsync(Connection connection, ushort channelId, params string[] names)
    {
        await connection.Client.SendAsync(new PqUserLogin(
            [.. names.Select(_ => new LoginAccountInfo($"player{Interlocked.Increment(ref _accounts)}@example.test", new Secret("digest"), 0, string.Empty))],
            new ConnectionAddress(0, 0),
            0));
        var login = Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        var characters = new List<CharacterInfo>();
        var appearances = new List<CharacterAppear>();
        for (var slot = 0; slot < names.Length; slot++)
        {
            await connection.Client.SendAsync(new PqNewCharacter((byte)slot, names[slot], new AppearanceSlot[AppearanceSlot.Count]));
            var created = (await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead)).Catalog!;
            characters.Add(Assert.Single(created.Characters));
            appearances.Add(Assert.Single(created.Appearances));
        }

        await connection.Client.SendAsync(new PqSelectCharacter([.. characters.Select(character => character.CharacterId)]));
        await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);

        var player = new Player(connection, login.LocalPlayerId, characters, appearances);
        if (channelId != 0)
        {
            Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(player, channelId));
        }

        return player;
    }

    /// <summary>Moves to a channel and reads the answer, with the play time ahead of an entered one.</summary>
    public static async Task<ChannelMoveResult> MoveAsync(Player player, ushort channelId)
    {
        await player.SendAsync(new PqChannelMoveTo(false, channelId, 0, 0));
        return (await player.Client.ReceiveMoveAnswerAsync()).Answer.Result;
    }

    /// <summary>
    /// Enters the lobby of the player's channel and returns the rooms it lists. Everyone in
    /// <paramref name="alreadyThere"/> is told of the player, which each reads here.
    /// </summary>
    public static async Task<IReadOnlyList<GameSessionInfoPack>> EnterLobbyAsync(Player player, params Player[] alreadyThere)
    {
        await player.SendAsync(new PqLobbyEnter(FromChannelMove: 0, ArenaTeamMode: 0xFF, ArenaTeamKey: 0xFFFFFFFF));
        Assert.Equal(LobbyEnterResult.Entered, (await player.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead)).Result);
        var rooms = (await player.ReceiveAsync<PcLobbyInitRoomList>(PcLobbyInitRoomList.TryRead)).Rooms;
        var listed = await player.ReceiveAsync<PcLobbyInitUserList>(PcLobbyInitUserList.TryRead);
        Assert.Subset(listed.Characters.Select(character => character.CharacterId).ToHashSet(), player.CharacterIds.ToHashSet());

        foreach (var other in alreadyThere)
        {
            var added = await other.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
            Assert.Equal(player.CharacterIds, added.Characters.Select(character => character.CharacterId));
        }

        return rooms;
    }

    /// <summary>A request for a room, as the client's create dialog makes one, with what a test changes.</summary>
    public static GameSessionInfo Requested(
        string name = "Phòng một",
        GameType gameType = GameType.TeamGame,
        GameMode gameMode = GameMode.Items,
        ChanceMode chanceMode = ChanceMode.Basic,
        string? password = null,
        byte maxPlayers = GameSlotInfo.SlotCount) => new(
            RoomNumber: 0,
            Status: RoomStatus.Requested,
            Grade: 0,
            Name: name,
            GameType: gameType,
            GameMode: gameMode,
            ChanceMode: chanceMode,
            CurrentPlayers: 0,
            MaxPlayers: maxPlayers,
            Password: password,
            MapId: 0);

    /// <summary>Asks to create a room.</summary>
    public static Task CreateAsync(Player host, GameSessionInfo? requested = null) =>
        host.SendAsync(new PqGameSessionCreate(requested ?? Requested(), Report));

    /// <summary>Asks to create a room, and reads the room it is sent.</summary>
    public static async Task<PcGameSessionInit> CreatedAsync(Player host, GameSessionInfo? requested = null)
    {
        await CreateAsync(host, requested);
        return await host.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead);
    }

    /// <summary>Asks to enter a room.</summary>
    public static Task EnterAsync(Player player, ushort roomNumber, string password = "") =>
        player.SendAsync(new PqGameSessionEnter(roomNumber, password, Report));

    /// <summary>Asks to enter a room, and reads the room it is sent.</summary>
    public static async Task<PcGameSessionInit> EnteredAsync(Player player, ushort roomNumber, string password = "")
    {
        await EnterAsync(player, roomNumber, password);
        return await player.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead);
    }

    /// <summary>
    /// Enters a room and reads the room it is sent; every one of <paramref name="members"/>
    /// reads that each of the entrant's slots is taken.
    /// </summary>
    public static async Task<PcGameSessionInit> SeatedAsync(Player entrant, ushort roomNumber, params Player[] members)
    {
        var entered = await EnteredAsync(entrant, roomNumber);
        var taken = entered.Slots.Where(slot => slot.Occupant is { } occupant && entrant.CharacterIds.Contains(occupant.CharacterId)).ToArray();
        Assert.Equal(entrant.Characters.Count, taken.Length);

        foreach (var member in members)
        {
            foreach (var slot in taken)
            {
                Assert.Equal(
                    new PcGameSessionUpdateSlot(SlotChange.Seated, slot, string.Empty),
                    await member.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
            }
        }

        return entered;
    }

    /// <summary>Asks to enter a room, and reads why it may not.</summary>
    public static async Task<EnterRefusal> RefusedEntryAsync(Player player, ushort roomNumber, string password = "")
    {
        await EnterAsync(player, roomNumber, password);
        return (await player.ReceiveAsync<PrGameSessionEnterFail>(PrGameSessionEnterFail.TryRead)).Reason;
    }

    /// <summary>Asks to join any room.</summary>
    public static Task QuickJoinAsync(Player player) =>
        player.SendAsync(new PqGameSessionQuickJoin(PqGameSessionQuickJoin.AnyRoom, Report));

    /// <summary>Asks to leave its room, and reads the answer.</summary>
    public static async Task<LeaveResult> LeaveAsync(Player player, ushort roomNumber = 0)
    {
        await player.SendAsync(new PqGameSessionLeave(roomNumber, Reason: 0));
        return (await player.ReceiveAsync<PrGameSessionLeave>(PrGameSessionLeave.TryRead)).Result;
    }

    /// <summary>
    /// Slot <paramref name="slot"/> holding the player's character at
    /// <paramref name="position"/>, as a room is sent it: not ready, with the default weapon.
    /// </summary>
    public static GameSlotInfo Seat(Player player, byte slot, int position = 0) => GameSlotInfo.Taken(
        slot,
        new SlotOccupant(
            player.PlayerId,
            player.Characters[position].CharacterId,
            player.Characters[position].Name,
            player.Characters[position].GuildName,
            player.Characters[position].Level,
            Seating.TeamOf(slot),
            player.Appearances[position],
            WeaponId: 0),
        ready: false);

    /// <summary>A room's eight slots: those given, the closed ones, and every other one open.</summary>
    public static IReadOnlyList<GameSlotInfo> Slots(IEnumerable<GameSlotInfo> taken, params byte[] closed)
    {
        var slots = Enumerable.Range(0, GameSlotInfo.SlotCount)
            .Select(slot => closed.Contains((byte)slot) ? GameSlotInfo.Closed((byte)slot) : GameSlotInfo.Open((byte)slot))
            .ToArray();
        foreach (var seat in taken)
        {
            slots[seat.SlotIndex] = seat;
        }

        return slots;
    }

    /// <summary>
    /// Nothing was sent to the player that it has not read: the next thing it receives is
    /// the answer to a request made now. What it is told while another player's request is
    /// handled has been sent by the time that player has its answer.
    /// </summary>
    public static async Task QuietAsync(params Player[] players)
    {
        foreach (var player in players)
        {
            await player.SendAsync(new PqGetChannelList());
            await player.ReceiveAsync<PrGetChannelList>(PrGetChannelList.TryRead);
        }
    }
}
