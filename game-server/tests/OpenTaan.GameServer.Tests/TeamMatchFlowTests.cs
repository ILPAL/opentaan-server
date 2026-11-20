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
        // the reference sessions' starting values, no captain, and no seed: only a mission
        // has one.
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
                CaptainSlots: [0xFF, 0xFF, 0xFF],
                MissionSeed: 0),
            start);
        await QuietAsync(host, guest);
    }

    [Theory]
    [InlineData(GameMode.Items)]
    [InlineData(GameMode.NoItems)]
    public async Task AMatchWithoutCaptainsNamesNoCaptainForEitherTeam(GameMode gameMode)
    {
        var services = Services();
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        await TeamRoomAsync(gameMode, host, second, third, fourth);

        var start = await StartedAsync(host, host, second, third, fourth);

        // Every slot from 0 to 3 holds a character, so a captain named for either team
        // would be one of them, and would fire every shot twice.
        Assert.Equal([(byte)0, 1, 2, 3], start.Players.Select(player => player.SlotIndex));
        Assert.Equal([(byte)0xFF, 0xFF, 0xFF], start.CaptainSlots);
        await QuietAsync(host, second, third, fourth);
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
        await TurnToldAsync(0, host, guest);
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
        await TurnToldAsync(0, host, guest);

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
        await TurnToldAsync(0, host, guest);
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
        await TurnToldAsync(0, host, guest);
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

        await TurnToldAsync(0, host, guest);

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

        await TurnToldAsync(0, host, second, third);
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
        await TurnToldAsync(0, everyone);

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
        await TurnToldAsync(0, first, second);

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

    [Theory]
    [InlineData(GameMode.Items)]
    [InlineData(GameMode.NoItems)]
    public async Task EveryTurnLetsAsManyItemBoxesDropAsTheServerIsSetToWithItemsOrWithout(GameMode gameMode)
    {
        // Without items, a box caught fills the catcher's team gauge instead, so boxes drop
        // just the same.
        var matches = SessionHarness.ShippedMatchOptions();
        matches.ItemBoxesPerTurn = 3;
        var services = Services(time: new ManualTime(), matches: matches);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(gameMode, host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Loading(0));

        await guest.SendAsync(Loading(2));

        foreach (var player in new[] { host, guest })
        {
            var start = await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead);
            Assert.Equal<(byte, byte)>((0, 3), (start.TurnSlot, start.ItemBoxBudget));
        }

        // The next turn too, which starts once the first has passed.
        var pass = new PcGamePlayPassTurn(0, 0x0102, 0x0304, 0x01, 0x05060708);
        await host.SendAsync(pass);
        Assert.Equal(pass, await guest.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
        foreach (var player in new[] { host, guest })
        {
            await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead);
            var start = await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead);
            Assert.Equal<(byte, byte)>((2, 3), (start.TurnSlot, start.ItemBoxBudget));
        }

        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task WithoutItemsABoxCaughtAndABonusCarryTheirTeamsGaugeInsteadAndGoToEveryClientItsSendersToo()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(GameMode.NoItems, host, guest);
        await StartedAsync(host, host, guest);
        await host.SendAsync(Loading(0));
        await guest.SendAsync(Loading(2));
        await TurnToldAsync(0, host, guest);

        // As a client sends them without items: no item, and the gauge its team gains, by
        // what it gains for, a box caught or a bonus.
        var caught = new PcGamePlayEatItemBox(
            0,
            new GameScenarioCommandEatItemBox(0, 0x0B, 0x0102, 0x0304, ItemBoxId: 0x0506070809, PickupItemId: 0, Reserved: 0x0A0B0C0D),
            new GameScenarioCommandTeamGauge(0, 0x0C, 0x1112, 0x1314, GaugeType: 3, NewGaugeValue: 0x0015, PreviousGaugeValue: 0x0010));
        var bonus = new PcGamePlayEatBonusItem(
            2,
            new GameScenarioCommandEatItemBox(2, 0x0B, 0x2122, 0x2324, ItemBoxId: ulong.MaxValue, PickupItemId: 0, Reserved: 0x2A2B2C2D),
            new GameScenarioCommandTeamGauge(2, 0x0C, 0x3132, 0x3334, GaugeType: 4, NewGaugeValue: 0x0025, PreviousGaugeValue: 0x0020));

        await host.SendAsync(caught);
        await EveryoneToldAsync(caught, PcGamePlayEatItemBox.TryRead, host, guest);
        await guest.SendAsync(bonus);
        await EveryoneToldAsync(bonus, PcGamePlayEatBonusItem.TryRead, host, guest);
        await QuietAsync(host, guest);
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
    public async Task AShotWhoseWeaponLeavesEffectsReachesTheOtherClientByteForByteAndTheMatchGoesOn()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);

        // As a weapon whose missiles carry effects fires it: a missile hits the guest, which
        // every client then gives the shot's effects. One effect's values are a NaN with a
        // payload and a negative zero, so a value passed on with any bit changed shows. They
        // go on the wire as bits, as a client sends them: a float built from them in code can
        // lose them first, since an optimizing compiler may work it out ahead.
        var shot = Encoded.Bytes(new PcGamePlayScenario(
            Direction: 0x01,
            FiringSlot: 0,
            new GameScenario(
                SlotIndex: 0,
                ActionType: 0x02,
                [
                    new GameScenarioCommandFire(0, 0x00, 0x0100, 0, 0x00, 0x03, 0x0201, 0x0302, 0x0100, 1f, 0x00, 0x0405, 0x01, 0x0201, 0x0302, 0x0201, 0x0302, 0x01, 0),
                    new GameScenarioCommandDamage(2, 0x04, 0x0200, 0x0300, 0x0102, 0x0304, 0x0506, 0x0708),
                    new GameScenarioCommand(0, 0x08, 0x0D0E, 0),
                ],
                [0x0102],
                [new WeaponEffect(0x02, 20f, 0f, 3, 0), new WeaponEffect(0x00, 0f, 0f, 0, 2)],
                WeaponFlags: 0x0F10,
                InitialAngle: 0x11121314,
                [2],
                Seed: 0x15161718),
            TotalStats: 0x191A1B1C,
            AmmoAddition: 0x1D1E,
            new IntegrityReport(0x1F202122, 0x23242526, 0x27, 0x28292A2B, 0x2C2D2E2F, 0x30313233)).Write);

        // The second effect's two values: after its id and its type, the only effect of type 0.
        byte[] secondEffectHead = [.. Encoded.UInt32(WeaponEffect.RegistryId), 0x00];
        var secondEffect = shot.AsSpan().IndexOf(secondEffectHead);
        Assert.True(secondEffect > 0);
        var amount = secondEffect + secondEffectHead.Length;
        Encoded.UInt32(0x7FA00001).CopyTo(shot, amount);
        Encoded.UInt32(0x80000000).CopyTo(shot, amount + sizeof(float));

        await host.Client.SendBodyAsync(shot);

        Assert.Equal(shot, await guest.Client.ReceiveBodyAsync());
        await QuietAsync(host, guest);

        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 2, turnCount: 0, host, guest);
        await QuietAsync(host, guest);
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
        await TurnToldAsync(0, host, guest);
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
