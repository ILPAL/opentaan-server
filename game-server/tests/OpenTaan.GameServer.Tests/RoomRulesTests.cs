using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>What a request to create a room must hold. Every value is invented.</summary>
public sealed class RoomRulesTests
{
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
}
