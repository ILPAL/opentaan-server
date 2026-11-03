using System.Net;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Data;

namespace OpenTaan.Tests.GameServer;

/// <summary>The channels and who is in them. Every value here is invented.</summary>
public sealed class ChannelDirectoryTests
{
    private static readonly SessionServices Services = SessionHarness.Services();

    private static readonly AdvertisedAddress Address = new(IPAddress.Parse("203.0.113.7"), 2190, AddressSource.Configured);

    [Fact]
    public void AConnectionIsInOneChannelAtATime()
    {
        var directory = Directory(capacity: 4);
        var session = NewSession(1);

        Assert.Equal(ChannelJoin.Joined, directory.TryJoin(session, 11));
        Assert.Equal(ChannelJoin.Joined, directory.TryJoin(session, 12));

        Assert.Equal((ushort)12, directory.ChannelOf(session));
        Assert.Equal((0u, 25u), (directory.Describe(11)!.FillPercent, directory.Describe(12)!.FillPercent));
    }

    [Fact]
    public void AFullChannelRefusesNewcomersButNotTheConnectionsAlreadyInIt()
    {
        var directory = Directory(capacity: 1);
        var inside = NewSession(1);
        directory.TryJoin(inside, 11);

        Assert.Equal(ChannelJoin.Full, directory.TryJoin(NewSession(2), 11));
        Assert.Equal(ChannelJoin.Joined, directory.TryJoin(inside, 11));
        Assert.Equal(100u, directory.Describe(11)!.FillPercent);
    }

    [Fact]
    public void LeavingFreesThePlace()
    {
        var directory = Directory(capacity: 1);
        var leaving = NewSession(1);
        directory.TryJoin(leaving, 11);

        directory.Leave(leaving);

        Assert.Null(directory.ChannelOf(leaving));
        Assert.Equal(ChannelJoin.Joined, directory.TryJoin(NewSession(2), 11));
    }

    [Fact]
    public void AClosedOrUnknownChannelCannotBeJoined()
    {
        var directory = Directory(capacity: 4);

        Assert.Equal(ChannelJoin.Closed, directory.TryJoin(NewSession(1), 13));
        Assert.Equal(ChannelJoin.Unknown, directory.TryJoin(NewSession(2), 14));
        Assert.Null(directory.Describe(14));

        var closed = directory.Describe(13)!;
        Assert.Equal(("Kênh C", (byte)8, (byte)1), (closed.Name, closed.Mode, closed.Disabled));
    }

    [Fact]
    public void TheListIsARootHoldingTheGroupHoldingEachChannel()
    {
        var directory = Directory(capacity: 4);
        directory.TryJoin(NewSession(1), 12);

        var expected = DataNode.Element(
            string.Empty,
            [],
            [
                DataNode.Element(
                    "server",
                    [new("name", "Gamma"), new("type", "4")],
                    [
                        Channel("11", "Kênh A", "0", "0", "6"),
                        Channel("12", "Kênh B", "25", "0", "6"),
                        Channel("13", "Kênh C", "0", "1", "8"),
                    ]),
            ]);

        // Nodes compare by reference, so the trees are compared as the bytes they write.
        Assert.Equal(expected.ToBytes(), directory.ListTree().ToBytes());
    }

    [Fact]
    public async Task OfManySimultaneousJoinsExactlyTheCapacityGetIn()
    {
        const int Contenders = 64;
        const int Capacity = 10;
        var directory = Directory(Capacity);
        var sessions = Enumerable.Range(1, Contenders).Select(id => NewSession(id)).ToArray();
        using var start = new Barrier(Contenders);

        // A thread each, not pool tasks: 64 pool threads blocked on the barrier would
        // starve every other test's timers and sockets.
        var results = await Task.WhenAll(sessions.Select(session => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return directory.TryJoin(session, 11);
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default)));

        Assert.Equal(Capacity, results.Count(result => result == ChannelJoin.Joined));
        Assert.Equal(100u, directory.Describe(11)!.FillPercent);
    }

    private static ChannelDirectory Directory(int capacity) => new(
        new ChannelOptions
        {
            Server = new ChannelServer { Name = "Gamma", Type = 4 },
            List =
            [
                new ChannelSettings { Id = 11, Name = "Kênh A", Capacity = capacity },
                new ChannelSettings { Id = 12, Name = "Kênh B", Capacity = capacity },
                new ChannelSettings { Id = 13, Name = "Kênh C", Mode = 8, Capacity = capacity, Disabled = true },
            ],
        },
        Address);

    private static DataNode Channel(string id, string name, string fill, string disable, string mode) =>
        DataNode.Element(
            "channel",
            [
                new("id", id),
                new("name", name),
                new("ip", "203.0.113.7"),
                new("port", "2190"),
                new("maxLevel", "0"),
                new("minLevel", "0"),
                new("fill", fill),
                new("disable", disable),
                new("mode", mode),
            ],
            []);

    private static ClientSession NewSession(long id) => new(id, null, Stream.Null, Services);
}
