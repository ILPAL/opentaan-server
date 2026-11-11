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
    public void TheShippedSettingsDrawEachRandomPickFromItsGroupOfTheClientsMapsAndTheTwoExtras()
    {
        var shipped = Shipped().GetSection(RoomOptions.Section).Get<RoomOptions>()!;
        IReadOnlyList<ushort> all = [.. Enumerable.Range(1, 21).Select(map => (ushort)map)];

        Assert.Equal(new ushort[] { 200, 201, 202, 203 }, shipped.RandomMaps.Keys.Order());
        Assert.Equal(all, shipped.RandomMaps[200]);
        Assert.Equal([(ushort)7, 8, 12, 14, 19, 20], shipped.RandomMaps[201]);
        Assert.Equal(all.Except(new ushort[] { 3, 6, 12, 15 }), shipped.RandomMaps[202]);
        Assert.Equal([(ushort)3, 6, 12, 13, 15, 21], shipped.RandomMaps[203]);
        Assert.Equal([(ushort)300, 500], shipped.RandomMapExtras);
        Assert.True(Validation(ChannelsOfModes(ChannelSettings.NormalMode)).Validate(null, shipped).Succeeded);

        // The harness the other tests run on stands in for the shipped settings.
        var harness = SessionHarness.ShippedRoomOptions();
        Assert.Equal(shipped.RandomMaps.Keys.Order(), harness.RandomMaps.Keys.Order());
        Assert.All(shipped.RandomMaps, group => Assert.Equal(group.Value, harness.RandomMaps[group.Key]));
        Assert.Equal(shipped.RandomMapExtras, harness.RandomMapExtras);
    }

    [Fact]
    public void ARandomPickDrawsFromTheMapsOfItsGroupTheChannelOffersThenTheExtrasEachOnce()
    {
        var rooms = new RoomOptions
        {
            RandomMaps = new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [7, 8, 12] },
            RandomMapExtras = [300, 7],
        };

        Assert.Equal([(ushort)7, 12, 300], rooms.RandomPool(201, [12, 7, 201]));
        Assert.Empty(rooms.RandomPool(202, [12, 7, 202]));
    }

    [Fact]
    public void ARandomPickWithOnlyTheExtrasToDrawFromIsAccepted()
    {
        var rooms = new RoomOptions
        {
            MapsByChannelMode = new Dictionary<byte, IReadOnlyList<ushort>> { [6] = [1, 201] },
            RandomMaps = new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [] },
            RandomMapExtras = [300],
        };

        Assert.True(Validation(ChannelsOfModes(6)).Validate(null, rooms).Succeeded);
    }

    [Theory]
    [InlineData("a group for a map that is no random pick", "gives a group to map 7:")]
    [InlineData("a random pick in a group", "never a random pick")]
    [InlineData("a random pick among the extras", "never a random pick")]
    [InlineData("a random pick the channel offers with no group", "leaves random pick 203, which channel mode 6 offers, no map to draw from")]
    [InlineData("a group with no map the channel offers and no extras", "leaves random pick 201, which channel mode 6 offers")]
    public void RandomMapsAreRefusedNamingWhatIsWrong(string settings, string named)
    {
        var rooms = new RoomOptions
        {
            MapsByChannelMode = new Dictionary<byte, IReadOnlyList<ushort>> { [6] = [1, 2, 201, 203] },
            RandomMaps = settings switch
            {
                "a group for a map that is no random pick" => new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [1], [203] = [2], [7] = [1] },
                "a random pick in a group" => new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [1, 203], [203] = [2] },
                "a random pick the channel offers with no group" => new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [1] },
                "a group with no map the channel offers and no extras" => new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [5], [203] = [2] },
                _ => new Dictionary<ushort, IReadOnlyList<ushort>> { [201] = [1], [203] = [2] },
            },
            RandomMapExtras = settings == "a random pick among the extras" ? [200] : [],
        };

        var result = Validation(ChannelsOfModes(6)).Validate(null, rooms);

        Assert.True(result.Failed);
        Assert.Contains(named, result.FailureMessage, StringComparison.Ordinal);
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
        Assert.Null(shipped.Problem());
        var harness = SessionHarness.ShippedMatchOptions();
        Assert.Equal(
            (shipped.LevelReadyWait, shipped.TurnWait, shipped.ShotWait, shipped.LongestShot, shipped.ResultWait),
            (harness.LevelReadyWait, harness.TurnWait, harness.ShotWait, harness.LongestShot, harness.ResultWait));
    }

    [Fact]
    public void TheShippedMatchSettingsWaitFiveSecondsForTheOthersOnceAClientHasLoadedAndThirtyInAll()
    {
        var shipped = Shipped().GetSection(MatchOptions.Section).Get<MatchOptions>()!;

        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)), (shipped.LoadWait, shipped.LongestLoad));
        var harness = SessionHarness.ShippedMatchOptions();
        Assert.Equal((shipped.LoadWait, shipped.LongestLoad), (harness.LoadWait, harness.LongestLoad));
    }

    [Theory]
    [InlineData(16_000)]
    [InlineData(10_000)]
    public void ALongestLoadNoLongerThanTheClientsTourOfAFullRoomIsRefusedNamingIt(int milliseconds)
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.LongestLoad = TimeSpan.FromMilliseconds(milliseconds);

        var result = new MatchOptionsValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Matches:LongestLoad", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("16 seconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongestLoadAnyLongerThanTheClientsTourOfAFullRoomIsAccepted()
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.LongestLoad = MatchRules.LongestClientTour + TimeSpan.FromMilliseconds(1);

        Assert.True(new MatchOptionsValidation().Validate(null, options).Succeeded);
    }

    [Fact]
    public void TheShippedMatchSettingsLetOneItemBoxDropATurnOfDropQualityTwenty()
    {
        var shipped = Shipped().GetSection(MatchOptions.Section).Get<MatchOptions>()!;

        Assert.Equal((1, 20u), (shipped.ItemBoxesPerTurn, shipped.ItemDropQuality));
        var harness = SessionHarness.ShippedMatchOptions();
        Assert.Equal((shipped.ItemBoxesPerTurn, shipped.ItemDropQuality), (harness.ItemBoxesPerTurn, harness.ItemDropQuality));
    }

    [Theory]
    [InlineData(101u)]
    [InlineData(uint.MaxValue)]
    public void ADropQualityAboveTheHighestTheClientKnowsIsRefusedNamingIt(uint quality)
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.ItemDropQuality = quality;

        var result = new MatchOptionsValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Matches:ItemDropQuality", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("0 to 100", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(MatchOptions.MaxDropQuality)]
    public void EveryDropQualityTheClientKnowsIsAccepted(uint quality)
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.ItemDropQuality = quality;

        Assert.True(new MatchOptionsValidation().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(15_900)]
    [InlineData(10_000)]
    public void ATurnWaitNoLongerThanTheClientsOwnTurnIsRefusedNamingIt(int milliseconds)
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.TurnWait = TimeSpan.FromMilliseconds(milliseconds);

        var result = new MatchOptionsValidation().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Matches:TurnWait", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("15.9 seconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ATurnWaitAnyLongerThanTheClientsOwnTurnIsAccepted()
    {
        var options = SessionHarness.ShippedMatchOptions();
        options.TurnWait = MatchRules.ClientTurnLength + TimeSpan.FromMilliseconds(1);

        Assert.True(new MatchOptionsValidation().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(nameof(MatchOptions.ShotWait))]
    [InlineData(nameof(MatchOptions.LoadWait))]
    [InlineData(nameof(MatchOptions.LongestLoad))]
    public void AWaitThatIsNotPositiveIsRefused(string wait)
    {
        var options = SessionHarness.ShippedMatchOptions();
        typeof(MatchOptions).GetProperty(wait)!.SetValue(options, TimeSpan.Zero);

        Assert.True(new MatchOptionsValidation().Validate(null, options).Failed);
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
