using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A mission's enemies' turns: which shots they follow, who is sent them and waited for,
/// and the waits that keep them moving, through real sessions on loopback sockets. Every
/// value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. Unless a test says otherwise, the host plays slot 0 and has the first turn,
/// the guest plays slot 1, and every level asks for its enemies to take turns, as every
/// level the client ships does. Every test runs on a clock that moves only when the test
/// moves it.
/// </remarks>
public sealed class EnemyTurnFlowTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task AShotEndsTheTurnThenTheEnemiesTakeTheirsOnEveryClientBeforeTheNextTurnStarts()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);

        // The enemies' turn is the level's turn count: one shot.
        await EnemiesTurnToldAsync(0, 1, host, guest);
        await QuietAsync(host, guest);

        // The next turn waits for every client.
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);
        await EnemiesTurnOverAsync(1, guest);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EachTurnOfTheEnemiesIsTheLevelsTurnCountWhichStartsAgainWithTheNextLevel()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAfterEnemiesAsync(0, 1, turnCount: 1, host, guest);
        await ShotAsync(guest, 1, host);
        await FinishedAsync(1, host, guest);
        await TurnPassedAfterEnemiesAsync(1, 0, turnCount: 2, host, guest);

        // The shot that clears the level begins the next, with no enemies' turn.
        await LevelClearedByShotAsync(host, 0, 2, host, guest);
        await QuietAsync(host, guest);
        await FirstTurnAsync(2, 1, host, guest);

        await ShotAsync(guest, 1, host);
        await FinishedAsync(1, host, guest);
        await TurnPassedAfterEnemiesAsync(1, 0, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AConnectionPlayingTwoCharactersIsSentTheEnemiesTurnOnceAndReportsItOnce()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await MissionRoomAsync(host, pair);
        await StartedAsync(host, host, pair);
        await LevelBegunAsync(1, host, pair);
        await FirstTurnAsync(1, 0, host, pair);
        await ShotAsync(host, 0, pair);
        await FinishedAsync(0, host, pair);

        // Once each, the pair's two characters notwithstanding: a second would move its enemies twice.
        await EnemiesTurnToldAsync(0, 1, host, pair);
        await QuietAsync(host, pair);

        // One report from the pair is all its client sends.
        await EnemiesTurnOverAsync(1, pair, host);
        await TurnStartedAsync(1, turnCount: 1, [], host, pair);
        await QuietAsync(host, pair);
    }

    [Fact]
    public async Task AClientWhoseCharactersAreAllGhostsIsSentTheEnemiesTurnAndWaitedFor()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        var (x, y) = MatchRules.StartPosition;
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await FinishedAsync(0, host, guest);

        await EnemiesTurnToldAsync(0, 1, host, guest);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);

        // The ghost's client still shows the field, and its enemies must stand where everyone's do.
        await EnemiesTurnOverAsync(1, guest);
        await TurnStartedAsync(0, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AReportOfAnotherTurnOfTheEnemiesOrASecondReportChangesNothing()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        await EnemiesTurnOverAsync(2, guest);
        await EnemiesTurnOverAsync(0, guest);
        await EnemiesTurnOverAsync(1, host);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);

        await EnemiesTurnOverAsync(1, guest);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AReportWhileTheEnemiesTakeNoTurnChangesNothing()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        // During a turn, and during a shot, whose wait it leaves as it was.
        await EnemiesTurnOverAsync(1, host, guest);
        await QuietAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await EnemiesTurnOverAsync(1, host, guest);
        await QuietAsync(host, guest);
        clock.Advance(SessionHarness.ShippedMatchOptions().EnemyTurnWait);
        await QuietAsync(host, guest);

        // The enemies' turn that follows waits for reports of its own.
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);
        await QuietAsync(host, guest);
        await EnemiesTurnOverAsync(1, host, guest);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
    }

    [Fact]
    public async Task InALevelWhoseEnemiesTakeNoTurnsAShotPassesTheTurnAtOnce()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Asking(1) with { WantsEnemyTurns = false });
        await guest.SendAsync(Asking(1) with { WantsEnemyTurns = false });
        await EveryoneToldAsync(new PcGameSessionStartMissionLevel(1), PcGameSessionStartMissionLevel.TryRead, host, guest);
        await FirstTurnAsync(1, 0, host, guest);

        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);

        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task NoCharacterTakesATurnNorDoesALevelBeginWhileTheEnemiesTakeTheirs()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        await host.SendAsync(Shot(0));
        await guest.SendAsync(Shot(1));
        await host.SendAsync(new PcGamePlayPassTurn(0, 0x0102, 0x0304, 0x01, 0x05060708));
        await guest.SendAsync(new PcGamePlayMove(1, 0x0102, 0x0304, 0x01));
        await host.SendAsync(new PcGamePlaySetPos(0, 0x0506, -0x0708, 0x00));
        await guest.SendAsync(Asking(2));
        await QuietAsync(host, guest);

        await EnemiesTurnOverAsync(1, host, guest);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheOthersAreGivenTheEnemyTurnWaitOnceTheFirstClientHasReported()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().EnemyTurnWait - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);

        // The guest's late report finds the next turn started.
        await EnemiesTurnOverAsync(1, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AnEnemiesTurnNoClientReportsOverEndsOnceTheLongestEnemyTurnIsOver()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().LongestEnemyTurn - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AFirstReportLateInTheEnemiesTurnLeavesTheOthersNoLongerThanTheLongestEnemyTurn()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().LongestEnemyTurn - Tick - Tick);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);
        clock.Advance(Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnStartedAsync(1, turnCount: 1, [], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AClientLeavingDuringTheEnemiesTurnIsNoLongerWaitedFor()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));

        // The guest's character left, so the host has the next turn too.
        await TurnStartedAsync(0, turnCount: 1, [], host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AClientLeavingBeforeAnyReportLeavesTheEnemiesTurnWaitingForTheRest()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));
        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.NotNull(await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await QuietAsync(host, guest);

        await EnemiesTurnOverAsync(1, host);
        await TurnStartedAsync(0, turnCount: 1, [], host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EveryPlayerGivingUpDuringTheEnemiesTurnLosesTheMission()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await EnemiesTurnToldAsync(0, 1, host, guest);

        // One player giving up leaves the enemies' turn waiting for every client still there.
        await host.SendAsync(new PcGamePlayGiveUp());
        await EnemiesTurnOverAsync(1, guest);
        await QuietAsync(host, guest);

        await guest.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);
        await EnemiesTurnOverAsync(1, host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMatchBetweenTeamsHasNoEnemiesTurn()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);

        await TeamTurnPassedAsync(0, 2, host, guest);
        await EnemiesTurnOverAsync(0, host, guest);
        await QuietAsync(host, guest);
    }
}
