using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>What a request to create a room must hold, and what its host may change it to. Every value is invented.</summary>
public sealed class RoomRulesTests
{
    // The maps a host may pick in a room of teams, for these tests.
    private static readonly IReadOnlyList<ushort> Maps = [3, 4, 5, 200, 201];

    /// <summary>A host's request each options rule refuses, with the kind of room it is made in, by what is wrong with it.</summary>
    internal static readonly IReadOnlyDictionary<string, (GameType Room, PqGameSessionChangeGameOption Request)> RefusedOptions =
        new Dictionary<string, (GameType, PqGameSessionChangeGameOption)>
        {
            ["a game mode that does not exist"] = (GameType.TeamGame, new(4, (GameMode)4, ChanceMode.Basic, 0xFF)),
            ["a chance mode that does not exist"] = (GameType.TeamGame, new(4, GameMode.Items, (ChanceMode)3, 0xFF)),
            ["a map the client does not list"] = (GameType.TeamGame, new(6, GameMode.Items, ChanceMode.Basic, 0xFF)),
            ["no map"] = (GameType.TeamGame, new(0, GameMode.Items, ChanceMode.Basic, 0xFF)),
            ["a captain mode in a mission room"] = (GameType.Mission, new(0, GameMode.CaptainItems, ChanceMode.Basic, 0)),
            ["a difficulty past the hardest"] = (GameType.Mission, new(0, GameMode.Items, ChanceMode.Basic, RoomRules.HardestDifficulty + 1)),
        };

    public static TheoryData<string> RefusedOptionRequests => [.. RefusedOptions.Keys];

    /// <summary>A request each rule refuses, by what is wrong with it.</summary>
    internal static readonly IReadOnlyDictionary<string, GameSessionInfo> Refused = new Dictionary<string, GameSessionInfo>
    {
        ["an empty name"] = Playing.Requested(name: string.Empty),
        ["a name of spaces"] = Playing.Requested(name: "   "),
        ["a name past the limit"] = Playing.Requested(name: new string('P', 129)),
        ["a password asked for and not given"] = Playing.Requested(password: string.Empty),
        ["a password longer than the client's field"] = Playing.Requested(password: "mật khẩu dài"),
        ["a game type that does not exist"] = Playing.Requested(gameType: (GameType)2),
        ["a game mode that does not exist"] = Playing.Requested(gameMode: (GameMode)4),
        ["a captain mode in a mission room"] = Playing.Requested(gameType: GameType.Mission, gameMode: GameMode.CaptainItems),
        ["a chance mode that does not exist"] = Playing.Requested(chanceMode: (ChanceMode)3),
        ["fewer players than slots"] = Playing.Requested(maxPlayers: 6),
        ["more players than slots"] = Playing.Requested(maxPlayers: 9),
    };

    public static TheoryData<string> RefusedRequests => [.. Refused.Keys];

    [Fact]
    public void TheRequestTheClientsDialogMakesIsAllowed() =>
        Assert.Null(RoomRules.Problem(Playing.Requested(), new RoomOptions()));

    [Fact]
    public void EveryGameModeChanceModeAndAPasswordAreAllowedInARoomOfTeams()
    {
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            foreach (var chance in Enum.GetValues<ChanceMode>())
            {
                Assert.Null(RoomRules.Problem(Playing.Requested(gameMode: mode, chanceMode: chance, password: "khóa"), new RoomOptions()));
            }
        }
    }

    [Fact]
    public void AMissionRoomPlaysTheFirstGameMode() =>
        Assert.Null(RoomRules.Problem(Playing.Requested(gameType: GameType.Mission, gameMode: GameMode.Items), new RoomOptions()));

    [Fact]
    public void ANameAndAPasswordAtTheirLimitsAreAllowed() =>
        Assert.Null(RoomRules.Problem(
            Playing.Requested(name: new string('P', 128), password: new string('k', RoomRules.MaxPasswordLength)),
            new RoomOptions()));

    [Fact]
    public void TheNameLimitIsTheConfiguredOne()
    {
        var options = new RoomOptions { MaxNameLength = 5 };

        Assert.Null(RoomRules.Problem(Playing.Requested(name: "Phòng"), options));
        Assert.NotNull(RoomRules.Problem(Playing.Requested(name: "Phòng 1"), options));
    }

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public void EachRuleRefusesWhatBreaksIt(string request) =>
        Assert.NotNull(RoomRules.Problem(Refused[request], new RoomOptions()));

    [Fact]
    public void ARoomOfTeamsTakesEveryModeAndChanceOnEveryMapTheClientLists()
    {
        foreach (var map in Maps)
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                foreach (var chance in Enum.GetValues<ChanceMode>())
                {
                    Assert.Null(RoomRules.OptionsProblem(GameType.TeamGame, new PqGameSessionChangeGameOption(map, mode, chance, 0xFF), Maps));
                }
            }
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0xFE)]
    [InlineData(0xFF)]
    public void ARoomOfTeamsLeavesTheDifficultyUnchecked(byte difficulty) =>
        Assert.Null(RoomRules.OptionsProblem(GameType.TeamGame, new PqGameSessionChangeGameOption(3, GameMode.Items, ChanceMode.Bonus, difficulty), Maps));

    [Fact]
    public void AMissionRoomTakesAnyMissionAtEachDifficultyTheClientOffers()
    {
        // A mission's number is not a map of the list, which covers rooms of teams alone.
        foreach (var mission in new ushort[] { 0, 6, 0xFFFF })
        {
            for (var difficulty = 0; difficulty <= RoomRules.HardestDifficulty; difficulty++)
            {
                Assert.Null(RoomRules.OptionsProblem(GameType.Mission, new PqGameSessionChangeGameOption(mission, GameMode.Items, ChanceMode.Basic, (byte)difficulty), Maps));
            }
        }
    }

    [Theory]
    [MemberData(nameof(RefusedOptionRequests))]
    public void EachOptionsRuleRefusesWhatBreaksIt(string request)
    {
        var (room, refused) = RefusedOptions[request];

        Assert.NotNull(RoomRules.OptionsProblem(room, refused, Maps));
    }

    [Fact]
    public void WithNoMapsListedARoomOfTeamsTakesNoMap() =>
        Assert.NotNull(RoomRules.OptionsProblem(GameType.TeamGame, new PqGameSessionChangeGameOption(3, GameMode.Items, ChanceMode.Basic, 0xFF), []));
}
