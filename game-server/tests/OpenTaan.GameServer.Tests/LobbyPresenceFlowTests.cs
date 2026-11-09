using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Who each player sees in a lobby, as players come and go, through real sessions on
/// loopback sockets. Every value here is invented.
/// </summary>
/// <remarks>
/// The client adds a character for every add it is sent, even one it already shows, and
/// removes one for every removal. So each test checks what a player was sent, add by add,
/// and that nothing more is on its way.
/// </remarks>
public sealed class LobbyPresenceFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static ChannelOptions Channels() => new()
    {
        Server = new ChannelServer { Name = "Gamma", Type = 4, PublicAddress = "203.0.113.7", PublicPort = 2190 },
        List =
        [
            new ChannelSettings { Id = 11, Name = "Kênh A", Capacity = 50 },
            new ChannelSettings { Id = 12, Name = "Kênh B", Capacity = 50 },
        ],
    };

    [Fact]
    public async Task AnEntrantSeesItselfInTheLobby()
    {
        var services = Services();
        await using var alone = await SessionHarness.ConnectAsync(services, Cancel);
        var character = await PlayAsync(alone, "alone@example.test", "Kimi");

        Assert.Equal([character], await EnterAsync(alone, 11));
        await QuietAsync(alone);
    }

    [Fact]
    public async Task AnEntrantSeesEveryoneAlreadyThereAndTheyAreToldOfIt()
    {
        var services = Services();
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        var firstCharacter = await PlayAsync(first, "first@example.test", "Kimi");
        var secondCharacter = await PlayAsync(second, "second@example.test", "Rồng");
        await EnterAsync(first, 11);

        Assert.Equal([firstCharacter, secondCharacter], (await EnterAsync(second, 11)).Order());

        var update = await first.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
        Assert.Equal([secondCharacter], update.Characters.Select(character => character.CharacterId));
        Assert.Equal([secondCharacter], update.Appearances.Select(appearance => appearance.Field08));
        Assert.Equal("Rồng", Assert.Single(update.Characters).Name);
        Assert.Empty(update.RemovedCharacterIds);
        await QuietAsync(first);
        await QuietAsync(second);
    }

    [Fact]
    public async Task LeavingTheLobbyTakesThePlayerOffEveryoneElsesList()
    {
        var services = Services();
        await using var staying = await SessionHarness.ConnectAsync(services, Cancel);
        await using var leaving = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(staying, "staying@example.test", "Kimi");
        var leaver = await PlayAsync(leaving, "leaving@example.test", "Rồng");
        await EnterAsync(staying, 11);
        await EnterAsync(leaving, 11);
        await staying.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);

        await leaving.Client.SendAsync(new PqLobbyLeave());
        await leaving.Client.ReceiveAsync<PrLobbyLeave>(PrLobbyLeave.TryRead);

        Assert.Equal([leaver], await RemovedAsync(staying));
        await QuietAsync(staying);
        await QuietAsync(leaving);
    }

    [Fact]
    public async Task AConnectionThatClosesInTheLobbyIsTakenOffEveryoneElsesList()
    {
        var services = Services();
        await using var staying = await SessionHarness.ConnectAsync(services, Cancel);
        await using var dropping = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(staying, "staying@example.test", "Kimi");
        var dropped = await PlayAsync(dropping, "dropping@example.test", "Rồng");
        await EnterAsync(staying, 11);
        await EnterAsync(dropping, 11);
        await staying.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);

        await dropping.Client.DisposeAsync();
        await dropping.ClosedReason();

        Assert.Equal([dropped], await RemovedAsync(staying));
        await QuietAsync(staying);
    }

    [Fact]
    public async Task MovingToAChannelTakesThePlayerOutOfTheLobbyItWasIn()
    {
        var services = Services();
        await using var staying = await SessionHarness.ConnectAsync(services, Cancel);
        await using var moving = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(staying, "staying@example.test", "Kimi");
        var mover = await PlayAsync(moving, "moving@example.test", "Rồng");
        await EnterAsync(staying, 11);
        await EnterAsync(moving, 11);
        await staying.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);

        Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(moving, 12));
        Assert.Equal([mover], await RemovedAsync(staying));

        Assert.Equal([mover], await EnterAsync(moving, 12, alreadyMoved: true));
        await QuietAsync(staying);
        await QuietAsync(moving);
    }

    [Fact]
    public async Task ARefusedMoveLeavesThePlayerInItsLobby()
    {
        var services = Services();
        await using var staying = await SessionHarness.ConnectAsync(services, Cancel);
        await using var following = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(staying, "staying@example.test", "Kimi");
        await PlayAsync(following, "following@example.test", "Rồng");
        await EnterAsync(staying, 11);
        await EnterAsync(following, 11);
        await staying.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);

        // Following a friend from the lobby, which is refused until friends exist.
        await following.Client.SendAsync(new PqChannelMoveTo(Forced: true, ChannelId: 0, Context: 0x0102, ClientVersionType: 0));
        Assert.Equal(ChannelMoveResult.ChannelClosed, (await following.Client.ReceiveAsync<PrChannelMoveTo>(PrChannelMoveTo.TryRead)).Result);

        await QuietAsync(staying);
    }

    [Fact]
    public async Task EnteringAgainWithoutLeavingListsTheLobbyAgainButAddsNoOneTwice()
    {
        var services = Services();
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        var firstCharacter = await PlayAsync(first, "first@example.test", "Kimi");
        var secondCharacter = await PlayAsync(second, "second@example.test", "Rồng");
        await EnterAsync(first, 11);
        await EnterAsync(second, 11);
        await first.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);

        Assert.Equal([firstCharacter, secondCharacter], (await EnterAsync(second, 11, alreadyMoved: true)).Order());

        await QuietAsync(first);
        await QuietAsync(second);
    }

    [Fact]
    public async Task TheLobbiesOfTwoChannelsDoNotSeeEachOther()
    {
        var services = Services();
        await using var first = await SessionHarness.ConnectAsync(services, Cancel);
        await using var second = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(first, "first@example.test", "Kimi");
        var secondCharacter = await PlayAsync(second, "second@example.test", "Rồng");
        await EnterAsync(first, 11);

        Assert.Equal([secondCharacter], await EnterAsync(second, 12));

        await second.Client.SendAsync(new PqLobbyLeave());
        await second.Client.ReceiveAsync<PrLobbyLeave>(PrLobbyLeave.TryRead);
        await QuietAsync(first);
    }

    [Fact]
    public async Task EveryCharacterAConnectionPlaysIsAddedAndRemovedTogether()
    {
        var services = Services();
        await using var watching = await SessionHarness.ConnectAsync(services, Cancel);
        await using var playingTwo = await SessionHarness.ConnectAsync(services, Cancel);
        await PlayAsync(watching, "watching@example.test", "Kimi");
        var both = await PlayAsync(playingTwo, ["one@example.test", "two@example.test"], ["Rồng", "Hoang"]);
        await EnterAsync(watching, 11);
        await EnterAsync(playingTwo, 11);

        var added = await watching.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
        Assert.Equal(both, added.Characters.Select(character => character.CharacterId));
        Assert.Equal(both, added.Appearances.Select(appearance => appearance.Field08));

        await playingTwo.Client.DisposeAsync();
        await playingTwo.ClosedReason();

        Assert.Equal(both, await RemovedAsync(watching));
        await QuietAsync(watching);
    }

    [Fact]
    public async Task PlayersEnteringAtOnceEachSeeEveryOtherOnce()
    {
        const int Players = 12;
        var services = Services();
        var connections = new List<Connection>();
        try
        {
            var characters = new List<uint>();
            for (var player = 0; player < Players; player++)
            {
                var connection = await SessionHarness.ConnectAsync(services, Cancel);
                connections.Add(connection);
                characters.Add(await PlayAsync(connection, $"player{player}@example.test", $"Player{player:D2}"));
            }

            var views = await Task.WhenAll(connections.Select(async connection =>
            {
                var seen = (await EnterAsync(connection, 11)).ToList();
                while (seen.Count < Players)
                {
                    var update = await connection.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
                    Assert.Empty(update.RemovedCharacterIds);
                    seen.AddRange(update.Characters.Select(character => character.CharacterId));
                }

                return seen;
            }));

            Assert.All(views, seen => Assert.Equal(characters.Order(), seen.Order()));
            foreach (var connection in connections)
            {
                await QuietAsync(connection);
            }
        }
        finally
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    [Fact]
    public void ALobbyTooLargeForOneFrameIsListedThenAddedInFramesThatFit()
    {
        var characters = Enumerable.Range(0, 600)
            .Select(index => Character((uint)(1000 + index), new string('W', 16), new string('G', 16)))
            .ToArray();

        var packets = LobbyRoster.ListOf(characters);

        Assert.IsType<PcLobbyInitUserList>(packets[0]);
        Assert.True(packets.Count > 1);
        Assert.All(packets.Skip(1), packet => Assert.Empty(Assert.IsType<PcLobbyUpdateUserList>(packet).RemovedCharacterIds));
        Assert.All(packets, packet => Assert.InRange(Encoded.Bytes(packet.Write).Length, 1, TransportLimits.MaxEncryptedBody));

        var listed = packets.SelectMany(packet => packet switch
        {
            PcLobbyInitUserList list => list.Characters,
            PcLobbyUpdateUserList update => update.Characters,
            _ => [],
        });
        Assert.Equal(characters.Select(character => character.Info), listed);
    }

    [Fact]
    public void ALobbyThatFitsOneFrameIsOneList()
    {
        var characters = new[] { Character(1001, "Kimi", string.Empty), Character(1002, "Rồng", "Hổ") };

        var list = Assert.IsType<PcLobbyInitUserList>(Assert.Single(LobbyRoster.ListOf(characters)));

        Assert.Equal(characters.Select(character => character.Info), list.Characters);
        Assert.Equal(characters.Select(character => character.Appearance), list.Appearances);
    }

    private static Character Character(uint characterId, string name, string guildName) => new(
        AccountId: characterId,
        Info: new CharacterInfo(characterId, name, 1, 0, 0, 300, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, guildName),
        Appearance: new CharacterAppear(Gender.Male, characterId, 1, new AppearanceSlot[AppearanceSlot.Count]));

    private static SessionServices Services() =>
        SessionHarness.Services(requireRegistration: false, store: new InMemoryAccountStore(0), channelOptions: Channels());

    // Moves to the channel unless it already has, enters its lobby, and returns who it lists.
    private static async Task<IReadOnlyList<uint>> EnterAsync(Connection connection, ushort channelId, bool alreadyMoved = false)
    {
        if (!alreadyMoved)
        {
            Assert.Equal(ChannelMoveResult.Entered, await MoveAsync(connection, channelId));
        }

        await connection.Client.SendAsync(new PqLobbyEnter(FromChannelMove: 0, ArenaTeamMode: 0xFF, ArenaTeamKey: 0xFFFFFFFF));
        Assert.Equal(new PrLobbyEnter(channelId, LobbyEnterResult.Entered), await connection.Client.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead));
        await connection.Client.ReceiveAsync<PcLobbyInitRoomList>(PcLobbyInitRoomList.TryRead);

        var list = await connection.Client.ReceiveAsync<PcLobbyInitUserList>(PcLobbyInitUserList.TryRead);
        Assert.Equal(list.Characters.Select(character => character.CharacterId), list.Appearances.Select(appearance => appearance.Field08));
        return [.. list.Characters.Select(character => character.CharacterId)];
    }

    private static async Task<IReadOnlyList<uint>> RemovedAsync(Connection connection)
    {
        var update = await connection.Client.ReceiveAsync<PcLobbyUpdateUserList>(PcLobbyUpdateUserList.TryRead);
        Assert.Empty(update.Characters);
        return update.RemovedCharacterIds;
    }

    private static async Task<ChannelMoveResult> MoveAsync(Connection connection, ushort channelId)
    {
        await connection.Client.SendAsync(new PqChannelMoveTo(false, channelId, 0, 0));
        return (await connection.Client.ReceiveMoveAnswerAsync()).Answer.Result;
    }

    // Nothing was sent to the connection that it has not read: the next thing it receives
    // is the answer to a request made now. Updates for it are sent while another
    // connection's request is handled, which has finished by the time a test gets here.
    private static async Task QuietAsync(Connection connection)
    {
        await connection.Client.SendAsync(new PqGetChannelList());
        await connection.Client.ReceiveAsync<PrGetChannelList>(PrGetChannelList.TryRead);
    }

    private static async Task<uint> PlayAsync(Connection connection, string emailAddress, string name) =>
        Assert.Single(await PlayAsync(connection, [emailAddress], [name]));

    // Logs in, creates a character on each account and selects them, as the client does
    // before its channel screen.
    private static async Task<IReadOnlyList<uint>> PlayAsync(Connection connection, string[] emailAddresses, string[] names)
    {
        await connection.Client.SendAsync(new PqUserLogin(
            [.. emailAddresses.Select(address => new LoginAccountInfo(address, new Secret("digest"), 0, string.Empty))],
            new ConnectionAddress(0, 0),
            0));
        Assert.IsType<PrUserLoginSucceeded>(await connection.Client.ReceiveAsync<PrUserLogin>(PrUserLogin.TryRead));

        var characters = new List<uint>();
        for (var slot = 0; slot < names.Length; slot++)
        {
            await connection.Client.SendAsync(new PqNewCharacter((byte)slot, names[slot], new AppearanceSlot[AppearanceSlot.Count]));
            var created = await connection.Client.ReceiveAsync<PrNewCharacter>(PrNewCharacter.TryRead);
            characters.Add(Assert.Single(created.Catalog!.Characters).CharacterId);
        }

        await connection.Client.SendAsync(new PqSelectCharacter(characters));
        await connection.Client.ReceiveAsync<PrSelectCharacter>(PrSelectCharacter.TryRead);
        return characters;
    }
}
