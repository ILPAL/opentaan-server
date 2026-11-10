using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>The room settings: what the server ships with, and what it refuses to start with.</summary>
public sealed class RoomSettingsTests
{
    [Fact]
    public void TheShippedSettingsLetAHostInAnOrdinaryChannelPickEveryMapAndRandomPickTheClientLists()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build()
            .GetSection(RoomOptions.Section)
            .Get<RoomOptions>()!;

        IReadOnlyList<ushort> expected = [.. Enumerable.Range(1, 21).Select(map => (ushort)map), 200, 201, 202, 203];
        Assert.Equal([ChannelSettings.NormalMode], shipped.MapsByChannelMode.Keys);
        Assert.Equal(expected, shipped.MapsFor(ChannelSettings.NormalMode));

        // The harness the other tests run on stands in for the shipped settings.
        var harness = SessionHarness.ShippedRoomOptions();
        Assert.Equal((shipped.MaxPerChannel, shipped.MaxNameLength), (harness.MaxPerChannel, harness.MaxNameLength));
        Assert.Equal(shipped.MapsByChannelMode.Keys, harness.MapsByChannelMode.Keys);
        Assert.Equal(expected, harness.MapsFor(ChannelSettings.NormalMode));
    }

    [Fact]
    public void EveryChannelsModeHavingMapsIsAccepted()
    {
        var rooms = new RoomOptions
        {
            MapsByChannelMode = new Dictionary<byte, IReadOnlyList<ushort>> { [6] = [1, 2], [4] = [3] },
        };

        Assert.True(Validation(ChannelsOfModes(6, 4, 6)).Validate(null, rooms).Succeeded);
    }

    [Fact]
    public void AChannelsModeWithoutMapsIsRefusedNamingTheMode()
    {
        var rooms = new RoomOptions
        {
            MapsByChannelMode = new Dictionary<byte, IReadOnlyList<ushort>> { [6] = [1, 2], [7] = [] },
        };

        var result = Validation(ChannelsOfModes(6, 7, 4)).Validate(null, rooms);

        Assert.True(result.Failed);
        Assert.Contains("channel mode 4, 7,", result.FailureMessage, StringComparison.Ordinal);
    }

    private static RoomMapsValidation Validation(ChannelOptions channels) => new(Options.Create(channels));

    private static ChannelOptions ChannelsOfModes(params byte[] modes) => new()
    {
        List = [.. modes.Select((mode, index) => new ChannelSettings { Id = (ushort)(index + 1), Name = $"Kênh {index + 1}", Mode = mode, Capacity = 10 })],
    };
}
