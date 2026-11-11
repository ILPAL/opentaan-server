using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A mission's turns, from a level's first turn: shots played back by every client, passes,
/// walks, deaths and ghosts, levels changing and the match ending, and the waits that keep
/// turns moving, through real sessions on loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. A request the match's rules do not allow is dropped, so for one of those,
/// that nothing is sent is what is checked. In every test the host plays slot 0 and has
/// the first turn, and the guest plays slot 1. Every test runs on a clock that moves only
/// when the test moves it, so a turn passes only for the reason the test gives it, never
/// because one of the server's waits ran out while the test waited.
/// </remarks>
public sealed class TurnFlowTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task AShotGoesToEveryOtherClientAsItCameAndNeverBackToItsShooter()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await ShotAsync(host, 0, guest);

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheTurnPassesOnceEveryClientHasPlayedTheShotBackAndOnlyOnce()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);

        // The shooter is first to finish, and the turn waits for the guest.
        await FinishedAsync(0, host);
        await QuietAsync(host, guest);

        await FinishedAsync(0, guest);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheTurnCountGoesUpWithEachShotInALevelAndStartsAgainWithTheNextLevel()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        // A shot counts.
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);

        // A pass does not, and neither does a turn the server passes.
        var pass = new PcGamePlayPassTurn(1, 0x0102, 0x0304, 0x01, 0x05060708);
        await guest.SendAsync(pass);
        Assert.Equal(pass, await host.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
        await TurnPassedAsync(1, 0, turnCount: 1, host, guest);
        clock.Advance(SessionHarness.ShippedMatchOptions().TurnWait);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);

        // The next shot counts on from there.
        await ShotAsync(guest, 1, host);
        await FinishedAsync(1, host, guest);
        await TurnPassedAsync(1, 0, turnCount: 2, host, guest);

        // The shot that clears the level counts toward it alone: the next level starts from none.
        await LevelClearedByShotAsync(host, 0, 2, host, guest);
        await FirstTurnAsync(2, 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AShotIsTakenOnlyForTheTurnsCharacterFromItsOwnClientAndOnceInATurn()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await guest.SendAsync(Shot(1));
        await guest.SendAsync(Shot(0));
        await QuietAsync(host, guest);

        await ShotAsync(host, 0, guest);
        await host.SendAsync(Shot(0));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AReportOfAShotNotBeingPlayedChangesNothing()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await FinishedAsync(0, host, guest);
        await QuietAsync(host, guest);

        await ShotAsync(host, 0, guest);
        await FinishedAsync(1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheOthersAreGivenTheShotWaitOnceTheFirstClientHasPlayedItBack()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host);
        await QuietAsync(host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().ShotWait - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);

        // The guest's late reports find the turn passed.
        await FinishedAsync(0, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AShotNoClientReportsPlayedBackEndsOnceTheLongestShotIsOver()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);

        // A shot's own wait, not the turn's, runs while it is played.
        clock.Advance(SessionHarness.ShippedMatchOptions().LongestShot - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task APassGoesToEveryOtherClientAndPassesTheTurnAtOnce()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var pass = new PcGamePlayPassTurn(0, 0x0102, -0x0304, 0x01, 0x05060708);

        await host.SendAsync(pass);

        Assert.Equal(pass, await guest.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
        await TurnPassedAsync(0, 1, host, guest);
        await QuietAsync(host, guest);

        // The host's turn is over, so its pass changes nothing.
        await host.SendAsync(pass);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task WalkingAndPlacingGoToEveryOtherClientOnlyForTheTurnsCharacter()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var move = new PcGamePlayMove(0, 0x0102, 0x0304, 0x01);
        var place = new PcGamePlaySetPos(0, 0x0506, -0x0708, 0x00);

        await host.SendAsync(move);
        Assert.Equal(move, await guest.ReceiveAsync<PcGamePlayMove>(PcGamePlayMove.TryRead));
        await host.SendAsync(place);
        Assert.Equal(place, await guest.ReceiveAsync<PcGamePlaySetPos>(PcGamePlaySetPos.TryRead));

        // Not the turn's character, and not the client's own.
        await guest.SendAsync(new PcGamePlayMove(1, 0x0102, 0x0304, 0x01));
        await host.SendAsync(new PcGamePlaySetPos(1, 0x0506, 0x0708, 0x00));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheServerPassesATurnWhoseCharacterNeitherShootsNorPassesInTime()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().TurnWait - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnPassedAsync(0, 1, host, guest);

        // A pass from a client that stopped answering, once it answers again, changes nothing.
        await host.SendAsync(new PcGamePlayPassTurn(0, 0, 0, 0, 0));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task EachTurnHasAWaitOfItsOwn()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var wait = SessionHarness.ShippedMatchOptions().TurnWait;

        clock.Advance(wait - Tick);
        await host.SendAsync(new PcGamePlayPassTurn(0, 0, 0, 0, 0));
        Assert.NotNull(await guest.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
        await TurnPassedAsync(0, 1, host, guest);

        // The first turn's wait would have ended now; the guest's turn waits from its start.
        clock.Advance(Tick);
        await QuietAsync(host, guest);

        clock.Advance(wait - (2 * Tick));
        await QuietAsync(host, guest);
        clock.Advance(Tick);
        await TurnPassedAsync(1, 0, host, guest);
    }

    [Fact]
    public async Task ALevelAskedForDuringAShotBeginsOnceEveryClientHasPlayedItBackWithoutATurnsEndOrStart()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);

        await host.SendAsync(Asking(2));
        await FinishedAsync(0, host);
        await QuietAsync(host, guest);

        await guest.SendAsync(Asking(2));
        await FinishedAsync(0, guest);
        await EveryoneToldAsync(new PcGameSessionStartMissionLevel(2), PcGameSessionStartMissionLevel.TryRead, host, guest);
        await QuietAsync(host, guest);

        // The next character has the new level's first turn, once every client has played its opening.
        await FirstTurnAsync(2, 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AClearAskedForDuringTheLastLevelsShotEndsTheMatchOnceEveryClientHasPlayedItBack()
    {
        // A mission of one level, so the first shot can clear it.
        var rooms = SessionHarness.ShippedRoomOptions();
        rooms.Missions = new Dictionary<ushort, byte> { [0] = 1 };
        var services = Services(rooms: rooms, time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);

        await host.SendAsync(Asking(PqGameSessionStartMissionLevel.NoNextLevel));
        await FinishedAsync(0, host);
        await QuietAsync(host, guest);

        await guest.SendAsync(Asking(PqGameSessionStartMissionLevel.NoNextLevel));
        await FinishedAsync(0, guest);
        await EndToldAsync(MatchRules.PlayersTeam, [host, guest], host, guest);
        await QuietAsync(host, guest);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(PqGameSessionStartMissionLevel.NoNextLevel)]
    public async Task ALaterLevelOrTheClearIsNotTakenWhileNoShotIsPlayed(byte level)
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await host.SendAsync(Asking(level));
        await QuietAsync(host, guest);

        // The shot that follows ends the turn as any other.
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
    }

    [Fact]
    public async Task ADeathInAShotMakesAGhostWhereTheCharacterStoodAndTheTurnsSkipIt()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await guest.SendAsync(new PcGamePlayLoadingDone([new LoadedCharacter(1, 0x0102, -0x0304)]));
        await ShotAsync(host, 0, guest);

        await guest.SendAsync(new PqGamePlayMakeGhost(1));

        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, 0x0102, -0x0304, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await QuietAsync(host, guest);

        // The guest's character is dead, so the host has the next turn too.
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMatchWhoseLastCharacterDiesInAShotIsLostOnceEveryClientHasPlayedItBack()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        var (x, y) = MatchRules.StartPosition;

        await host.SendAsync(new PqGamePlayMakeGhost(0));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await QuietAsync(host, guest);

        await FinishedAsync(0, host, guest);

        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AFallGoesToEveryOtherClientMakesAGhostWhereTheCharacterLastStoodAndPassesItsTurn()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var move = new PcGamePlayMove(0, 0x0102, 0x0304, 0x01);
        await host.SendAsync(move);
        Assert.Equal(move, await guest.ReceiveAsync<PcGamePlayMove>(PcGamePlayMove.TryRead));
        var fall = new PcGamePlayDeathByDrop(0, 0x0506, 0x0708);

        await host.SendAsync(fall);

        Assert.Equal(fall, await guest.ReceiveAsync<PcGamePlayDeathByDrop>(PcGamePlayDeathByDrop.TryRead));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, 0x0102, 0x0304, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await TurnPassedAsync(0, 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostIsMadeOnceAndOnlyForAClientsOwnCharacter()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        var (x, y) = MatchRules.StartPosition;

        await guest.SendAsync(new PqGamePlayMakeGhost(0));
        await QuietAsync(host, guest);

        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheTurnsCharacterLeavingPassesItsTurn()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await host.SendAsync(new PcGamePlayPassTurn(0, 0, 0, 0, 0));
        Assert.NotNull(await guest.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
        await TurnPassedAsync(0, 1, host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await TurnPassedAsync(1, 0, host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AClientLeavingDuringAShotIsNoLongerWaitedFor()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));

        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await TurnPassedAsync(0, 0, turnCount: 1, host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task GivingUpDuringOnesOwnTurnPassesIt()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        await host.SendAsync(new PcGamePlayGiveUp());

        await TurnPassedAsync(0, 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task NothingAboutTurnsIsAnsweredOnceTheMatchIsOver()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        // The guest first, so the turn stays the host's until the end.
        await guest.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);

        await host.SendAsync(Shot(0));
        await host.SendAsync(new PcGamePlayPassTurn(0, 0, 0, 0, 0));
        await host.SendAsync(new PcGamePlayMove(0, 0, 0, 0));
        await FinishedAsync(0, host, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        await host.SendAsync(new PcGamePlayDeathByDrop(0, 0, 0));
        await QuietAsync(host, guest);
    }
}
