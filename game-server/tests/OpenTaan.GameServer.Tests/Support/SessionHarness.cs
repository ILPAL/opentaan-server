using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.PlayTime;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Transport;

namespace OpenTaan.Tests.GameServer;

/// <summary>A running session and the client connected to it.</summary>
internal sealed record Connection(ClientSession Session, LoopbackClient Client, Task<CloseReason> Closed) : IAsyncDisposable
{
    /// <summary>Why the session ended, failing the test if it has not ended within a few seconds.</summary>
    public async Task<CloseReason> ClosedReason() => await Closed.WaitAsync(TimeSpan.FromSeconds(10));

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

/// <summary>Runs real sessions over loopback sockets.</summary>
internal static class SessionHarness
{
    private static long _nextId;

    /// <summary>The character settings the server ships with, as <c>appsettings.json</c> sets them.</summary>
    public static CharacterOptions ShippedCharacterOptions() => new()
    {
        CreatorParts = [1, 3, 5, 8],
        Start = new CharacterStart { ExperienceCeiling = 300 },
    };

    /// <summary>The channel settings the server ships with, as <c>appsettings.json</c> sets them.</summary>
    public static ChannelOptions ShippedChannelOptions() => new()
    {
        Server = new ChannelServer { Name = "Alpha", Type = 1 },
        List = [.. Enumerable.Range(1, 5).Select(id => new ChannelSettings { Id = (ushort)id, Name = $"Channel {id}", Capacity = 100 })],
    };

    /// <summary>The room settings the server ships with, as <c>appsettings.json</c> sets them.</summary>
    public static RoomOptions ShippedRoomOptions() => new()
    {
        MaxPerChannel = 500,
        MaxNameLength = 128,
        MapsByChannelMode = new Dictionary<byte, IReadOnlyList<ushort>>
        {
            [ChannelSettings.NormalMode] = [.. Enumerable.Range(1, 21).Select(map => (ushort)map), 200, 201, 202, 203],
        },
        Missions = new Dictionary<ushort, byte> { [0] = 4 },
    };

    /// <summary>The match settings the server ships with, as <c>appsettings.json</c> sets them.</summary>
    public static MatchOptions ShippedMatchOptions() => new()
    {
        LevelReadyWait = TimeSpan.FromSeconds(10),
        TurnWait = TimeSpan.FromSeconds(25),
        ShotWait = TimeSpan.FromSeconds(5),
        LongestShot = TimeSpan.FromSeconds(60),
        ResultWait = TimeSpan.FromSeconds(25),
    };

    /// <summary>What sessions share, with stores and settings a test may choose.</summary>
    public static SessionServices Services(
        bool requireRegistration = true,
        IReadOnlyList<string>? gameMasters = null,
        IAccountStore? store = null,
        GameServerOptions? options = null,
        HandshakeKeySource? handshakeKeys = null,
        ICharacterStore? characterStore = null,
        CharacterOptions? characterOptions = null,
        ChannelOptions? channelOptions = null,
        PortraitOptions? portraitOptions = null,
        PlayTimeOptions? playTimeOptions = null,
        TimeProvider? time = null,
        RoomOptions? roomOptions = null,
        MatchOptions? matchOptions = null,
        Random? random = null)
    {
        characterStore ??= new InMemoryCharacterStore();
        var accounts = new AccountHandlers(
            store ?? new InMemoryAccountStore(),
            characterStore,
            Options.Create(new AccountOptions { RequireRegistration = requireRegistration, GameMasters = gameMasters ?? [] }),
            NullLogger<AccountHandlers>.Instance);
        var characters = new CharacterHandlers(
            characterStore,
            Options.Create(characterOptions ?? ShippedCharacterOptions()),
            NullLogger<CharacterHandlers>.Instance);

        options ??= new GameServerOptions();
        channelOptions ??= ShippedChannelOptions();
        // Never this machine's own address, so no test depends on the network it runs on.
        var address = AdvertisedAddress.Choose(
            channelOptions.Server.PublicAddress,
            options.BindAddress,
            (ushort)(channelOptions.Server.PublicPort ?? options.Port),
            () => IPAddress.Loopback);
        var channels = new ChannelDirectory(channelOptions, address);
        var lobbies = new LobbyRoster();
        var roomSettings = Options.Create(roomOptions ?? ShippedRoomOptions());
        var rooms = new RoomDirectory(
            lobbies,
            roomSettings,
            Options.Create(matchOptions ?? ShippedMatchOptions()),
            time ?? TimeProvider.System,
            random ?? new Random(1),
            NullLogger<RoomDirectory>.Instance);

        var names = new PacketNames();
        var dispatcher = new PacketDispatcher(
            accounts,
            characters,
            new ChannelHandlers(
                channels,
                lobbies,
                rooms,
                new PlayTimeGrant(Options.Create(playTimeOptions ?? new PlayTimeOptions()), time ?? TimeProvider.System),
                NullLogger<ChannelHandlers>.Instance),
            new LobbyHandlers(
                channels,
                lobbies,
                rooms,
                characterStore,
                Options.Create(portraitOptions ?? new PortraitOptions()),
                NullLogger<LobbyHandlers>.Instance),
            new RoomHandlers(rooms, channels, characterStore, roomSettings, NullLogger<RoomHandlers>.Instance),
            new MatchHandlers(rooms, NullLogger<MatchHandlers>.Instance),
            names,
            Options.Create(options),
            NullLogger<PacketDispatcher>.Instance);

        return new SessionServices(
            dispatcher,
            names,
            options,
            handshakeKeys ?? (() => HandshakeKeys.Generate(RandomNumberGenerator.Create())),
            new LoggedInAccounts(),
            channels,
            lobbies,
            rooms,
            NullLogger<ClientSession>.Instance);
    }

    /// <summary>
    /// Opens a loopback connection, runs a session on the server's end, and reads the
    /// handshake on the client's.
    /// </summary>
    /// <param name="services">What the session uses.</param>
    /// <param name="stopping">Stands in for server shutdown.</param>
    /// <param name="wrap">Wraps the server's stream, to stand in for a misbehaving network.</param>
    public static async Task<Connection> ConnectAsync(
        SessionServices services,
        CancellationToken stopping,
        Func<Stream, Stream>? wrap = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connecting = clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndpoint, stopping);
        var serverSocket = await listener.AcceptSocketAsync(stopping);
        await connecting;
        listener.Stop();

        Stream stream = new NetworkStream(serverSocket, ownsSocket: true);
        var session = new ClientSession(Interlocked.Increment(ref _nextId), serverSocket.RemoteEndPoint, wrap?.Invoke(stream) ?? stream, services);
        var closed = Task.Run(() => session.RunAsync(stopping), CancellationToken.None);

        var client = await LoopbackClient.AttachAsync(clientSocket);
        return new Connection(session, client, closed);
    }
}
