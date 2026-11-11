using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A match between teams, from its host's start to its end: who may start one, the map it
/// is played on, the clients loading it, the teams taking turns, and a team losing once it
/// has nobody left in play, through real sessions on loopback sockets. Every value is
/// invented.
/// </summary>
/// <remarks>
/// Each test checks what every player was sent, packet by packet, and that nothing more is
/// on its way. A request the match's rules do not allow is dropped, so for one of those,
/// that nothing is sent is what is checked. Two players play one against one: the host in
/// slot 0, on the first team, and the guest in slot 2, on the second. Four play two against
/// two: the host in slot 0, then the next three in slots 2, 1 and 3, as the room seats each
/// on the team with fewer characters. A test of a wait runs on a clock that moves only when
/// the test moves it.
/// </remarks>
public sealed class TeamMatchFlowTests
{
    private const uint Seed = 0x5EED5EED;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    [Fact]
    public async Task AHostStartsAMatchBetweenTeamsOfTheSameSizeAndEveryoneIsToldHowEachCharacterStarts()
    {
        var services = Services(random: new ScriptedChance(Seed));
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);

        var start = await StartedAsync(host, host, guest);

        // The map every new room of teams has, one entry for each character on either team,
        // the reference sessions' starting values, and no seed: only a mission has one.
        Assert.Equal(
            new PcGameSessionStartGame(
                MapId: 1,
                Players:
                [
                    new GameSlotPlayInfo(0, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                    new GameSlotPlayInfo(2, 200, 982, false, 1000, 1000, 0, 0, 100, 0, 0),
                ],
                Wind: 0,
                Field24: 0,
                CaptainSlots: [0, 1, 0],
                MissionSeed: 0),
            start);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AStartIsDroppedUnlessBothTeamsHoldAsManyCharacters()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        var number = (await CreatedAsync(host)).Room.RoomNumber;

        // One against none.
        await host.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host);

        // One against two: the pair sits together on the other team.
        await SeatedAsync(pair, number, host);
        await pair.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(pair, host, pair);
        await host.SendAsync(new PqGameSessionStartGame());
        await QuietAsync(host, pair);
    }

    [Theory]
    [InlineData(GameMode.CaptainItems)]
    [InlineData(GameMode.CaptainNoItems)]
    public async Task AStartIsDroppedInARoomWithCaptains(GameMode gameMode)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(gameMode, host, guest);

        await host.SendAsync(new PqGameSessionStartGame());

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMapPickedIsPlayedAsPicked()
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await MapPickedAsync(host, 7, host, guest);

        var start = await StartedAsync(host, host, guest);

        Assert.Equal(7, start.MapId);
    }

    [Fact]
    public async Task ARandomPickIsPlayedOnAMapDrawnEvenlyFromItsGroupAndTheExtrasAndTheRoomStillShowsIt()
    {
        var chance = new ScriptedChance(Seed) { Draw = 6 };
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await using var late = await ArriveAsync(services, ChannelId, "Linh");
        await TeamRoomAsync(host, guest);
        await MapPickedAsync(host, 201, host, guest);
        await EnterLobbyAsync(late);

        var start = await StartedAsync(host, host, guest);

        // The six maps of its group the channel offers, then the two extras: the seventh is
        // the first extra.
        Assert.Equal([8], chance.DrawnAmong);
        Assert.Equal(300, start.MapId);
        var change = Assert.Single((await late.ReceiveAsync<PcLobbyUpdateRoomList>(PcLobbyUpdateRoomList.TryRead)).Changes);
        Assert.Equal((RoomStatus.Playing, (ushort)201), (change.Room!.Status, change.Room.MapId));
        await QuietAsync(host, guest, late);
    }

    [Fact]
    public async Task EachMatchOnARandomPickDrawsItsMapAgain()
    {
        var chance = new ScriptedChance(Seed);
        var services = Services(random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await MapPickedAsync(host, 201, host, guest);
        Assert.Equal(7, (await StartedAsync(host, host, guest)).MapId);
        await guest.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(1, [new SeatOf(host, 0), new SeatOf(guest, 2)], host, guest);
        await PickAsync(host, host, guest, card: 0, slot: 0);
        await PickAsync(guest, host, guest, card: 1, slot: 2);
        await guest.SendAsync(new PqGameSessionReady(IsReady: true));
        await ReadyToldAsync(guest, host, guest);
        chance.Draw = 1;

        var start = await StartedAsync(host, host, guest);

        Assert.Equal(8, start.MapId);
    }

    [Fact]
    public async Task TheFirstTurnWaitsForEveryClientToReportTheMatchLoadedAndGoesToTheLowestSlot()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await guest.SendAsync(Loading(2));
        await QuietAsync(host, guest);

        await host.SendAsync(Loading(0));
        await TurnToldAsync(0, GameMode.Items, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheOthersAreGivenTheLoadWaitOnceTheFirstClientHasReported()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await guest.SendAsync(Loading(2));
        await QuietAsync(host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().LoadWait - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnToldAsync(0, GameMode.Items, host, guest);

        // The client that reports once the turn has started changes nothing, nor does a second report.
        await host.SendAsync(Loading(0));
        await guest.SendAsync(Loading(2));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheFirstTurnStartsOnceTheLongestLoadIsOverWhenNoClientReports()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        clock.Advance(SessionHarness.ShippedMatchOptions().LongestLoad - Tick);
        await QuietAsync(host, guest);

        clock.Advance(Tick);
        await TurnToldAsync(0, GameMode.Items, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheLoadWaitNeverRunsPastTheLongestLoad()
    {
        var clock = new ManualTime();
        var services = Services(time: clock);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        var left = TimeSpan.FromSeconds(2);
        clock.Advance(SessionHarness.ShippedMatchOptions().LongestLoad - left);

        await guest.SendAsync(Loading(2));
        await QuietAsync(host, guest);

        clock.Advance(left - Tick);
        await QuietAsync(host, guest);
        clock.Advance(Tick);
        await TurnToldAsync(0, GameMode.Items, host, guest);
    }

    [Fact]
    public async Task AReportNamingAnotherPlayersSlotStillCountsItsClientAsLoaded()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Loading(0));

        await guest.SendAsync(new PcGamePlayLoadingDone([new LoadedCharacter(0, 0x0102, 0x0304)]));

        await TurnToldAsync(0, GameMode.Items, host, guest);

        // Where it said the host's character stands was not taken: the host's ghost is where
        // the host's own client last put it.
        await host.SendAsync(new PcGamePlayDeathByDrop(0, 0x0506, 0x0708));
        await guest.ReceiveAsync<PcGamePlayDeathByDrop>(PcGamePlayDeathByDrop.TryRead);
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, MatchRules.StartX, MatchRules.StartY, 1), PcGamePlayMakeGhost.TryRead, host, guest);
    }

    [Fact]
    public async Task AClientLeavingWhileTheOthersLoadIsNoLongerWaitedFor()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        await TeamRoomAsync(host, second, third, fourth);
        await StartedAsync(host, host, second, third, fourth);
        await host.SendAsync(Loading(0));
        await second.SendAsync(Loading(2));
        await third.SendAsync(Loading(1));
        await QuietAsync(host, second, third, fourth);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(fourth));

        foreach (var player in new[] { host, second, third })
        {
            Assert.Equal(new PcGamePlayLeaveUser(3), await player.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
            Assert.Equal(
                new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(3), string.Empty),
                await player.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        }

        await TurnToldAsync(0, GameMode.Items, host, second, third);
        await QuietAsync(host, second, third, fourth);
    }

    [Fact]
    public async Task TheTeamsTakeTurnsEachGoingThroughItsCharactersInSlotOrder()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(host, second, third, fourth);
        await StartedAsync(host, everyone);
        await host.SendAsync(Loading(0));
        await second.SendAsync(Loading(2));
        await third.SendAsync(Loading(1));
        await fourth.SendAsync(Loading(3));
        await TurnToldAsync(0, GameMode.Items, everyone);

        await PassedAsync(host, 0, 2, everyone);
        await PassedAsync(second, 2, 1, everyone);
        await PassedAsync(third, 1, 3, everyone);
        await PassedAsync(fourth, 3, 0, everyone);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task BothCharactersOfAConnectionPlayForTheirTeamEachInItsTurn()
    {
        var services = Services(time: new ManualTime());
        await using var first = await ArriveAsync(services, ChannelId, "Kimi", "Linh");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await TeamRoomAsync(first, second);

        var start = await StartedAsync(first, first, second);
        Assert.Equal([(byte)0, 1, 2, 3], start.Players.Select(player => player.SlotIndex));
        await first.SendAsync(Loading(0, 1));
        await second.SendAsync(Loading(2, 3));
        await TurnToldAsync(0, GameMode.Items, first, second);

        await PassedAsync(first, 0, 2, first, second);
        await PassedAsync(second, 2, 1, first, second);
        await PassedAsync(first, 1, 3, first, second);
        await QuietAsync(first, second);
    }

    [Fact]
    public async Task ACharacterDyingInAShotBecomesAGhostOfItsTeamAndATeamWithNobodyLeftLosesOnceTheShotIsPlayed()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);
        await ShotAsync(host, 0, guest);

        await guest.SendAsync(new PqGamePlayMakeGhost(2));

        await EveryoneToldAsync(new PcGamePlayMakeGhost(2, MatchRules.StartX, MatchRules.StartY, 2), PcGamePlayMakeGhost.TryRead, host, guest);
        await QuietAsync(host, guest);

        await FinishedAsync(0, host, guest);
        await EndToldAsync(1, [new SeatOf(host, 0), new SeatOf(guest, 2)], host, guest);
        await QuietAsync(host, guest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AShotTakingTheLastOfBothTeamsOutIsWonByTheShootersTeam(bool guestShoots)
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);
        var hostAt = (X: MatchRules.StartX, Y: MatchRules.StartY);
        if (guestShoots)
        {
            // The host's pass says where its character stands.
            await PassedAsync(host, 0, 2, host, guest);
            hostAt = (0x0102, 0x0304);
        }

        var (shooter, other, slot, team) = guestShoots ? (guest, host, (byte)2, (byte)2) : (host, guest, (byte)0, (byte)1);
        await ShotAsync(shooter, slot, other);
        await host.SendAsync(new PqGamePlayMakeGhost(0));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, hostAt.X, hostAt.Y, 1), PcGamePlayMakeGhost.TryRead, host, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(2));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(2, MatchRules.StartX, MatchRules.StartY, 2), PcGamePlayMakeGhost.TryRead, host, guest);

        await FinishedAsync(slot, host, guest);

        await EndToldAsync(team, [new SeatOf(host, 0), new SeatOf(guest, 2)], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AFallTakingATeamsLastCharacterOutEndsTheMatchAtOnce()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);
        var fall = new PcGamePlayDeathByDrop(0, 0x0506, 0x0708);

        await host.SendAsync(fall);

        Assert.Equal(fall, await guest.ReceiveAsync<PcGamePlayDeathByDrop>(PcGamePlayDeathByDrop.TryRead));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, MatchRules.StartX, MatchRules.StartY, 1), PcGamePlayMakeGhost.TryRead, host, guest);
        await EndToldAsync(2, [new SeatOf(host, 0), new SeatOf(guest, 2)], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ATeamLosesOnceEveryOneOfItsPlayersHasGivenUp()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        Player[] everyone = [host, second, third, fourth];
        await TeamRoomAsync(host, second, third, fourth);
        await StartedAsync(host, everyone);

        // The first team still has the host.
        await third.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(everyone);

        await host.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(2, [new SeatOf(host, 0), new SeatOf(second, 2), new SeatOf(third, 1), new SeatOf(fourth, 3)], everyone);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task ATeamLosesWhenItsLastPlayerLeaves()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));

        Assert.Equal(new PcGamePlayLeaveUser(2), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(2), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));
        await EndToldAsync(1, [new SeatOf(host, 0)], host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AMatchWithoutItemsLetsNoItemBoxDrop()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.NoItems, host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Loading(0));

        await guest.SendAsync(Loading(2));

        var turn = await host.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead);
        Assert.Equal(0, turn.ItemBoxBudget);
        Assert.Equal(MatchRules.TurnOf(0, turnCount: 0, GameMode.NoItems, SessionHarness.ShippedMatchOptions(), []), turn);
        Assert.Equal(turn, await guest.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead));
    }

    [Fact]
    public async Task TheTurnCountStaysAtNoneBetweenTeams()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);

        await ShotAsync(host, 0, guest);
        await FinishedAsync(0, host, guest);

        await TurnPassedAsync(0, 2, turnCount: 0, host, guest);
    }

    [Fact]
    public async Task AMissionsRequestsAreDroppedInAMatchBetweenTeams()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        // While the clients load, when a mission asks for its first level.
        await host.SendAsync(Asking(1));
        await guest.SendAsync(new PqGameSessionMissionLevelReady(1));
        await QuietAsync(host, guest);

        // The match's first turn still starts once both have loaded, and its turns are dropped too.
        await host.SendAsync(Loading(0));
        await guest.SendAsync(Loading(2));
        await TurnToldAsync(0, GameMode.Items, host, guest);
        await host.SendAsync(Asking(1));
        await guest.SendAsync(new PqGameSessionMissionLevelReady(0));
        await host.SendAsync(Asking(PqGameSessionStartMissionLevel.NoNextLevel));
        await QuietAsync(host, guest);
    }

    // The host picks map, and every one of players is told the room's new options.
    private static async Task MapPickedAsync(Player host, ushort map, params Player[] players)
    {
        await host.SendAsync(new PqGameSessionChangeGameOption(map, GameMode.Items, ChanceMode.Bonus, 0xFF));
        await EveryoneToldAsync(new PcGameSessionChangeGameOption(map, GameMode.Items, ChanceMode.Bonus, 0xFF), PcGameSessionChangeGameOption.TryRead, players);
    }
}
