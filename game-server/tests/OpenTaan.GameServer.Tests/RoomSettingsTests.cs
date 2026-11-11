using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Matches;
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

    [Fact]
    public void TheShippedSettingsListTheMissionEveryNewMissionRoomPlaysWithItsFourLevels()
    {
        var shipped = Shipped().GetSection(RoomOptions.Section).Get<RoomOptions>()!;

        Assert.Equal(new Dictionary<ushort, byte> { [0] = 4 }, shipped.Missions);
        Assert.Null(shipped.MissionsProblem());
        Assert.Equal(shipped.Missions, SessionHarness.ShippedRoomOptions().Missions);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("without the first")]
    [InlineData("a mission of no levels")]
    [InlineData("a mission of more levels than the client can ask for")]
    public void MissionsAreRefusedWithoutTheFirstOrWithALevelCountTheClientCannotPlay(string missions)
    {
        var rooms = new RoomOptions
        {
            Missions = missions switch
            {
                "none" => new Dictionary<ushort, byte>(),
                "without the first" => new Dictionary<ushort, byte> { [1] = 5 },
                "a mission of no levels" => new Dictionary<ushort, byte> { [0] = 4, [1] = 0 },
                _ => new Dictionary<ushort, byte> { [0] = 255 },
            },
        };

        Assert.NotNull(rooms.MissionsProblem());
    }

    [Fact]
    public void AMissionMayHaveAsManyLevelsAsTheClientCanAskFor()
    {
        var rooms = new RoomOptions { Missions = new Dictionary<ushort, byte> { [0] = 1, [7] = RoomOptions.MaxMissionLevels } };

        Assert.Null(rooms.MissionsProblem());
    }

    [Fact]
    public void TheShippedMatchSettingsWaitTenSecondsForALevelTwentyFiveForATurnFiveAndSixtyForAShotAndTwentyFiveForTheCards()
    {
        var shipped = Shipped().GetSection(MatchOptions.Section).Get<MatchOptions>()!;

        Assert.Equal(
            (TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(25)),
            (shipped.LevelReadyWait, shipped.TurnWait, shipped.ShotWait, shipped.LongestShot, shipped.ResultWait));
        Assert.Null(shipped.WaitsProblem());
        var harness = SessionHarness.ShippedMatchOptions();
        Assert.Equal(
            (shipped.LevelReadyWait, shipped.TurnWait, shipped.ShotWait, shipped.LongestShot, shipped.ResultWait),
            (harness.LevelReadyWait, harness.TurnWait, harness.ShotWait, harness.LongestShot, harness.ResultWait));
    }

    [Theory]
    [InlineData(15_900)]
    [InlineData(10_000)]
    public void ATurnWaitNoLongerThanTheClientsOwnTurnIsRefusedNamingIt(int milliseconds)
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.TurnWait = TimeSpan.FromMilliseconds(milliseconds);

        var result = new MatchWaitsValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Matches:TurnWait", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("15.9 seconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ATurnWaitAnyLongerThanTheClientsOwnTurnIsAccepted()
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.TurnWait = MatchRules.ClientTurnLength + TimeSpan.FromMilliseconds(1);

        Assert.True(new MatchWaitsValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void AWaitThatIsNotPositiveIsRefused()
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.ShotWait = TimeSpan.Zero;

        Assert.True(new MatchWaitsValidation().Validate(null, options).Failed);
    }

    private static IConfiguration Shipped() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
        .Build();

    private static RoomMapsValidation Validation(ChannelOptions channels) => new(Options.Create(channels));

    private static ChannelOptions ChannelsOfModes(params byte[] modes) => new()
    {
        List = [.. modes.Select((mode, index) => new ChannelSettings { Id = (ushort)(index + 1), Name = $"Kênh {index + 1}", Mode = mode, Capacity = 10 })],
    };
}
