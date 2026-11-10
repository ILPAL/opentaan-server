using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Rooms with connections that play two accounts at once, which no recording has, through
/// real sessions on loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// The client keeps the one player id its login was answered with, and tells its own
/// slots, and the host's, by it. So both of a connection's characters carry that id, never
/// each account's own, and they always sit together on one team.
/// </remarks>
public sealed class TwoAccountRoomTests
{
    [Fact]
    public async Task APairHostsFromTheFirstTwoSlotsUnderThePlayerIdItsLoginWasAnsweredWith()
    {
        var services = Services();
        await using var pair = await ArriveAsync(services, ChannelId, "Kimi", "Rồng");

        var room = await CreatedAsync(pair);

        Assert.Equal(Slots([Seat(pair, 0, 0), Seat(pair, 1, 1)]), room.Slots);
        Assert.All(room.Slots.Take(2), slot => Assert.Equal(pair.PlayerId, slot.Occupant!.PlayerId));
        Assert.Equal(new GameSessionOption(pair.PlayerId, pair.PlayerId), room.Option);
        Assert.Equal((byte)2, room.Room.CurrentPlayers);
        await QuietAsync(pair);
    }

    [Fact]
    public async Task APairSitsTogetherOnTheTeamWithFewer()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var room = await CreatedAsync(host);

        var entered = await SeatedAsync(pair, room.Room.RoomNumber, host);

        Assert.Equal(Slots([Seat(host, 0), Seat(pair, 2, 0), Seat(pair, 3, 1)]), entered.Slots);
        Assert.Equal((byte)2, entered.Difficulty);
        await QuietAsync(host, pair);
    }

    [Fact]
    public async Task APairIsRefusedARoomWithOneFreeSlot()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi", "Rồng");
        await using var guest = await ArriveAsync(services, ChannelId, "Hoang");
        await using var pair = await ArriveAsync(services, ChannelId, "Linh", "Minh");
        var room = await CreatedAsync(host, Requested(gameType: GameType.Mission));
        await SeatedAsync(guest, room.Room.RoomNumber, host);

        Assert.Equal(EnterRefusal.RoomFull, await RefusedEntryAsync(pair, room.Room.RoomNumber));
        await QuietAsync(host, guest, pair);
    }

    [Fact]
    public async Task APairIsNeverSplitAcrossTheTeams()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi", "Rồng");
        await using var second = await ArriveAsync(services, ChannelId, "Hoang");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Minh");
        await using var fifth = await ArriveAsync(services, ChannelId, "Tuan");
        await using var pair = await ArriveAsync(services, ChannelId, "Nhung", "Phong");
        var room = await CreatedAsync(host);
        var number = room.Room.RoomNumber;
        await SeatedAsync(second, number, host);
        await SeatedAsync(third, number, host, second);
        await SeatedAsync(fourth, number, host, second, third);
        var entered = await SeatedAsync(fifth, number, host, second, third, fourth);

        // One slot is left on each team.
        Assert.Equal(
            Slots([Seat(host, 0, 0), Seat(host, 1, 1), Seat(second, 2), Seat(third, 3), Seat(fourth, 4), Seat(fifth, 6)]),
            entered.Slots);

        Assert.Equal(EnterRefusal.SlotUnavailable, await RefusedEntryAsync(pair, number));
        await QuickJoinAsync(pair);
        await pair.ReceiveAsync<PrGameSessionQuickJoinFail>(PrGameSessionQuickJoinFail.TryRead);
        await QuietAsync(host, second, third, fourth, fifth, pair);

        // A room with space for both is chosen instead.
        await using var other = await ArriveAsync(services, ChannelId, "Quang");
        var otherRoom = await CreatedAsync(other);
        await QuickJoinAsync(pair);
        var joined = await pair.ReceiveAsync<PcGameSessionInit>(PcGameSessionInit.TryRead);
        Assert.Equal(otherRoom.Room.RoomNumber, joined.Room.RoomNumber);
        Assert.Equal(Slots([Seat(other, 0), Seat(pair, 2, 0), Seat(pair, 3, 1)]), joined.Slots);
    }

    [Fact]
    public async Task APairLeavingFreesBothSlotsAndIsAnsweredOnce()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var room = await CreatedAsync(host);
        await SeatedAsync(pair, room.Room.RoomNumber, host);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(pair));

        foreach (var slot in new byte[] { 2, 3 })
        {
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(slot), string.Empty),
                await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        await QuietAsync(host, pair);
    }

    [Fact]
    public async Task APairWhoseConnectionClosesFreesBothSlots()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var room = await CreatedAsync(host);
        await SeatedAsync(pair, room.Room.RoomNumber, host);

        await pair.DisposeAsync();
        await pair.Connection.ClosedReason();

        foreach (var slot in new byte[] { 2, 3 })
        {
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(slot), string.Empty),
                await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        await QuietAsync(host);
    }

    [Fact]
    public async Task APairHandedTheRoomIsToldOnceThatItIsNotReadyThenThatItHosts()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var room = await CreatedAsync(host);
        await SeatedAsync(pair, room.Room.RoomNumber, host);

        await host.DisposeAsync();
        await host.Connection.ClosedReason();

        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(0), string.Empty),
            await pair.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        Assert.Equal(new PcGameSessionReady(pair.PlayerId, IsReady: false), await pair.ReceiveAsync<PcGameSessionReady>(PcGameSessionReady.TryRead));
        Assert.Equal(
            new GameSessionOption(pair.PlayerId, pair.PlayerId),
            (await pair.ReceiveAsync<PcGameSessionUpdateOption>(PcGameSessionUpdateOption.TryRead)).Option);
        await QuietAsync(pair);
    }
}
