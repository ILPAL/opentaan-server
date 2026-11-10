using System.Buffers.Binary;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Rooms;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Players acting on rooms at the same moment, through real sessions on loopback sockets.
/// Every value is invented.
/// </summary>
/// <remarks>
/// Each race is run many times over, on fresh servers, so that the requests meet in as
/// many orders as the machine produces.
/// </remarks>
public sealed class RoomRaceTests
{
    private const int Rounds = 20;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoPlayersRacingForTheLastSlotSeatOneAndRefuseTheOther()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var services = Services();
            await using var host = await ArriveAsync(services, ChannelId, "Kimi", "Rồng");
            await using var guest = await ArriveAsync(services, ChannelId, "Hoang");
            await using var first = await ArriveAsync(services, ChannelId, "Linh");
            await using var second = await ArriveAsync(services, ChannelId, "Minh");
            var room = await CreatedAsync(host, Requested(gameType: GameType.Mission));
            var number = room.Room.RoomNumber;
            await SeatedAsync(guest, number, host);

            await Task.WhenAll(EnterAsync(first, number), EnterAsync(second, number));
            var answers = await Task.WhenAll(AnswerAsync(first), AnswerAsync(second));

            var seated = Assert.IsType<PcGameSessionInit>(Assert.Single(answers, answer => answer is PcGameSessionInit));
            Assert.Equal(EnterRefusal.RoomFull, Assert.IsType<PrGameSessionEnterFail>(Assert.Single(answers, answer => answer is PrGameSessionEnterFail)).Reason);
            var lastSlot = seated.Slots[5];
            Assert.Equal(SlotStatus.Seated, lastSlot.Status);
            foreach (var member in new[] { host, guest })
            {
                Assert.Equal(
                    new PcGameSessionUpdateSlot(SlotChange.Seated, lastSlot, string.Empty),
                    await member.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
            }

            await QuietAsync(host, guest, first, second);
        }
    }

    [Fact]
    public async Task APlayerEnteringTheLobbyAsARoomOpensSeesItExactlyOnce()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var services = Services();
            await using var host = await ArriveAsync(services, ChannelId, "Kimi");
            await using var watcher = await ArriveAsync(services, ChannelId, "Rồng");

            var entering = watcher.SendAsync(new PqLobbyEnter(FromChannelMove: 0, ArenaTeamMode: 0xFF, ArenaTeamKey: 0xFFFFFFFF));
            var created = CreatedAsync(host);
            await entering;
            var room = await created;

            await watcher.ReceiveAsync<PrLobbyEnter>(PrLobbyEnter.TryRead);
            var listed = (await watcher.ReceiveAsync<PcLobbyInitRoomList>(PcLobbyInitRoomList.TryRead)).Rooms.Count;
            await watcher.ReceiveAsync<PcLobbyInitUserList>(PcLobbyInitUserList.TryRead);

            // The host is in a room by now, and every change it made has been sent.
            foreach (var body in await UntilQuietAsync(watcher))
            {
                var update = Assert.Single(Read(PcLobbyUpdateRoomList.TryRead, body).Changes);
                Assert.Equal(room.Room.RoomNumber, update.RoomNumber);
                listed++;
            }

            Assert.Equal(1, listed);
        }
    }

    [Fact]
    public async Task RoomsCreatedAtOnceNeverPassTheChannelsLimit()
    {
        const int Hosts = 8;
        const int Limit = 3;
        var services = Services(new RoomOptions { MaxPerChannel = Limit });
        var hosts = new List<Player>();
        try
        {
            for (var host = 0; host < Hosts; host++)
            {
                hosts.Add(await ArriveAsync(services, ChannelId, $"Host{host:D2}"));
            }

            await Task.WhenAll(hosts.Select(host => CreateAsync(host)));
            var answers = await Task.WhenAll(hosts.Select(async host =>
            {
                var body = await host.Client.ReceiveBodyAsync();
                return BinaryPrimitives.ReadUInt32LittleEndian(body) == PcGameSessionInit.RegistryId;
            }));

            Assert.Equal(Limit, answers.Count(created => created));
            await QuietAsync([.. hosts]);
        }
        finally
        {
            foreach (var host in hosts)
            {
                await host.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task AMemberThatStopsReadingIsClosedAndLeavesItsRoomOnce()
    {
        // Room enough for the most a player is sent at once, the play time and the answer
        // to a channel move, and little enough that the stalled member fills it soon.
        const int QueueCapacity = 8;
        var services = Services(options: new GameServerOptions { SendQueueCapacity = QueueCapacity });
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var churner = await ArriveAsync(services, ChannelId, "Hoang");
        PausableStream? stalled = null;
        await using var stalling = await ArriveAsync(
            await SessionHarness.ConnectAsync(services, Cancel, stream => stalled = new PausableStream(stream)),
            ChannelId,
            "Rồng");
        var room = await CreatedAsync(host);
        var number = room.Room.RoomNumber;
        await SeatedAsync(stalling, number, host);
        stalled!.Pause();

        // Every time someone comes and goes, the member that stopped reading is told, until
        // its queue is full and it is closed.
        for (var round = 0; round < QueueCapacity + 4; round++)
        {
            await EnteredAsync(churner, number);
            await churner.SendAsync(new PqGameSessionLeave(number, Reason: 0));
            while (await churner.Client.ReceiveBodyAsync() is var body
                   && BinaryPrimitives.ReadUInt32LittleEndian(body) != PrGameSessionLeave.RegistryId)
            {
                // A slot freed by the member that stopped reading, while the churner sat.
                Assert.Equal(SlotChange.Vacated, Read(PcGameSessionUpdateSlot.TryRead, body).Change);
            }
        }

        Assert.Equal(CloseReason.SendQueueFull, await stalling.Connection.ClosedReason());
        Assert.Null(services.Rooms.RoomOf(stalling.Connection.Session));

        // The host was told of the member that stopped reading leaving exactly once, and of
        // everyone else as they came and went.
        var occupants = new Dictionary<byte, uint> { [0] = host.CharacterIds[0], [2] = stalling.CharacterIds[0] };
        var left = new List<uint>();
        foreach (var body in await UntilQuietAsync(host))
        {
            var update = Read(PcGameSessionUpdateSlot.TryRead, body);
            if (update.Change == SlotChange.Seated)
            {
                occupants.Add(update.Slot.SlotIndex, update.Slot.Occupant!.CharacterId);
            }
            else
            {
                Assert.True(occupants.Remove(update.Slot.SlotIndex, out var characterId));
                left.Add(characterId);
            }
        }

        Assert.Single(left, characterId => characterId == stalling.CharacterIds[0]);
        Assert.Equal(host.CharacterIds[0], Assert.Single(occupants).Value);
    }

    private static async Task<object> AnswerAsync(Player player)
    {
        var body = await player.Client.ReceiveBodyAsync();
        return BinaryPrimitives.ReadUInt32LittleEndian(body) == PcGameSessionInit.RegistryId
            ? Read(PcGameSessionInit.TryRead, body)
            : Read(PrGameSessionEnterFail.TryRead, body);
    }

    // Everything the player was sent before the answer to a request made now.
    private static async Task<IReadOnlyList<byte[]>> UntilQuietAsync(Player player)
    {
        await player.SendAsync(new PqGetChannelList());
        var bodies = new List<byte[]>();
        while (await player.Client.ReceiveBodyAsync() is var body
               && BinaryPrimitives.ReadUInt32LittleEndian(body) != PrGetChannelList.RegistryId)
        {
            bodies.Add(body);
        }

        return bodies;
    }

    private static TPacket Read<TPacket>(PacketDecoder<TPacket> decode, byte[] body)
        where TPacket : class
    {
        var read = decode(body);
        Assert.True(read.Succeeded, $"expected {typeof(TPacket).Name}, which refused the body: {read.Error}");
        return read.Packet;
    }
}
