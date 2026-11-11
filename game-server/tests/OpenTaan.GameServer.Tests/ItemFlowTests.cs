using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// A mission's items and ghosts, as the clients decide them: each item packet passed on to
/// the clients that act on it, only for a client's own characters and only while a match
/// runs; a ghost moved by the start of every turn until it reaches where its player sent
/// it; and the item settings the start of every turn carries. Through real sessions on
/// loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// As in the turn tests, the host plays slot 0 and has the first turn, the guest plays slot
/// 1, every test runs on a clock that moves only when the test moves it, and a packet the
/// server drops is checked by nothing reaching anyone.
/// </remarks>
public sealed class ItemFlowTests
{
    private static readonly GameScenarioCommandTeamGauge Gauge = new(0, 0x0C, 0x0102, 0x0304, 0x01, 0x0506, 0x0708);

    [Fact]
    public async Task AnItemUsedSpentThrownAwayOrTakenOffGoesToEveryClientItsSendersToo()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var use = new PcGamePlayUseItem(0, MatchRules.PlayersTeam, 0x00, 0x01, 0x02, 0x0304);
        var spend = new PcGamePlaySpendItem(0, MatchRules.PlayersTeam, 0xFF, [new SpentItem(0x0506, 0x07), new SpentItem(0x0809, 0x0A)]);
        var forsake = new PcGamePlayForsakeItem(0, 0x01, 0x0B0C);
        var takenOff = new PcGamePlayForsakeActiveItem(0, MatchRules.PlayersTeam, 0x01, 0x00, 0x0D0E);

        await host.SendAsync(use);
        await EveryoneToldAsync(use, PcGamePlayUseItem.TryRead, host, guest);
        await host.SendAsync(spend);
        await EveryoneToldAsync(spend, PcGamePlaySpendItem.TryRead, host, guest);
        await host.SendAsync(forsake);
        await EveryoneToldAsync(forsake, PcGamePlayForsakeItem.TryRead, host, guest);
        await host.SendAsync(takenOff);
        await EveryoneToldAsync(takenOff, PcGamePlayForsakeActiveItem.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ABoxCaughtAndABonusGoToEveryClientItsSendersTooWithTheItemItsClientDrew()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var caught = new PcGamePlayEatItemBox(0, CatchOf(0, box: 0x0102030405, item: 0x0607), Gauge);
        var bonus = new PcGamePlayEatBonusItem(1, CatchOf(1, box: 0, item: 0x0809), TeamGauge: null);

        await host.SendAsync(caught);
        await EveryoneToldAsync(caught, PcGamePlayEatItemBox.TryRead, host, guest);
        await guest.SendAsync(bonus);
        await EveryoneToldAsync(bonus, PcGamePlayEatBonusItem.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task WhereAnItemWentGoesBackToItsSenderAlone()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var went = new PcGamePlayNotifyEatItem(1, 0x02, 0x0304);

        await guest.SendAsync(went);

        Assert.Equal(went, await guest.ReceiveAsync<PcGamePlayNotifyEatItem>(PcGamePlayNotifyEatItem.TryRead));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AClientsItemsArePassedOnWhoeverHasTheTurnAndWhileAShotIsPlayedBack()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var use = new PcGamePlayUseItem(1, MatchRules.PlayersTeam, 0x00, 0x00, 0xFF, 0x0102);

        // The host has the turn.
        await guest.SendAsync(use);
        await EveryoneToldAsync(use, PcGamePlayUseItem.TryRead, host, guest);

        // A step of the host's shot has the guest's client use an item as it plays the shot back.
        await ShotAsync(host, 0, guest);
        await guest.SendAsync(use);
        await EveryoneToldAsync(use, PcGamePlayUseItem.TryRead, host, guest);

        // The shot ends as any other.
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AnItemPacketNamingACharacterTheClientDoesNotPlayIsDropped()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);

        // The host's character, and a slot no character sits in.
        foreach (var slot in new byte[] { 0, 5 })
        {
            await guest.SendAsync(new PcGamePlayUseItem(slot, MatchRules.PlayersTeam, 0x00, 0x00, 0x01, 0x0203));
            await guest.SendAsync(new PcGamePlaySpendItem(slot, MatchRules.PlayersTeam, 0xFF, [new SpentItem(0x0203, 0x04)]));
            await guest.SendAsync(new PcGamePlayForsakeItem(slot, 0x01, 0x0203));
            await guest.SendAsync(new PcGamePlayForsakeActiveItem(slot, MatchRules.PlayersTeam, 0x00, 0x01, 0x0203));
            await guest.SendAsync(new PcGamePlayEatItemBox(slot, CatchOf(slot, box: 0x05, item: 0x0203), TeamGauge: null));
            await guest.SendAsync(new PcGamePlayEatBonusItem(slot, CatchOf(slot, box: 0, item: 0x0203), TeamGauge: null));
            await guest.SendAsync(new PcGamePlayNotifyEatItem(slot, 0x01, 0x0203));
        }

        // No character at all, which the client never sends.
        await guest.SendAsync(new PcGamePlaySpendItem(PcGamePlaySpendItem.NoSlot, MatchRules.PlayersTeam, 0xFF, [new SpentItem(0x0203, 0x04)]));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task ItemsAndGhostsAreDroppedFromAClientInNoMatchWhichStaysConnected()
    {
        var services = Services(time: new ManualTime());

        // A client playing the tutorial, which it plays by itself, in no room.
        await using var alone = await ArriveAsync(services, ChannelId, "Kimi");
        await alone.SendAsync(new PcGamePlayNotifyEatItem(0, 0x01, 0x0203));
        await alone.SendAsync(new PcGamePlaySetGhostPos(0, 0x0405, 0x0607, 0x01));

        // Players in a room whose match has not started.
        await using var host = await ArriveAsync(services, ChannelId, "Rồng");
        await using var guest = await ArriveAsync(services, ChannelId, "Linh");
        await MissionRoomAsync(host, guest);
        await host.SendAsync(new PcGamePlayUseItem(0, MatchRules.PlayersTeam, 0x00, 0x00, 0x01, 0x0203));
        await guest.SendAsync(new PcGamePlayEatBonusItem(1, CatchOf(1, box: 0, item: 0x0203), TeamGauge: null));
        await guest.SendAsync(new PcGamePlaySetGhostTarget(1, 0x0405, 0x0607));

        await QuietAsync(alone, host, guest);
    }

    [Fact]
    public async Task NothingAboutItemsOrGhostsIsPassedOnOnceTheMatchIsOver()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await guest.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(MatchRules.MissionsTeam, [host, guest], host, guest);

        await host.SendAsync(new PcGamePlayUseItem(0, MatchRules.PlayersTeam, 0x00, 0x00, 0x01, 0x0203));
        await host.SendAsync(new PcGamePlayNotifyEatItem(0, 0x01, 0x0203));
        await guest.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, box: 0x05, item: 0x0203), TeamGauge: null));
        await guest.SendAsync(new PcGamePlaySetGhostTarget(1, 0x0405, 0x0607));
        await guest.SendAsync(new PcGamePlaySetGhostPos(1, 0x0405, 0x0607, 0x01));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostIsMovedByTheStartOfEveryTurnFromWhereItLastStoodUntilItsClientReportsItThere()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await guest.SendAsync(new PcGamePlayLoadingDone([new LoadedCharacter(1, 0x0102, -0x0304)]));
        await GuestDiesInHostsShotAsync(host, guest, at: (0x0102, -0x0304));

        // The guest's player sends its ghost off, and the host has the next turn, the guest's character being dead.
        var aim = new PcGamePlaySetGhostTarget(1, 0x0506, -0x0708);
        await guest.SendAsync(aim);
        await EveryoneToldAsync(aim, PcGamePlaySetGhostTarget.TryRead, host, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, [new GhostMove(1, 0x0102, -0x0304, 0x0506, -0x0708)], host, guest);

        // The ghost stops short, and the next turn sends it on from there.
        var stopped = new PcGamePlaySetGhostPos(1, 0x0405, -0x0607, 0x01);
        await guest.SendAsync(stopped);
        Assert.Equal(stopped, await host.ReceiveAsync<PcGamePlaySetGhostPos>(PcGamePlaySetGhostPos.TryRead));
        await QuietAsync(host, guest);
        await HostPassesAsync(host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, [new GhostMove(1, 0x0405, -0x0607, 0x0506, -0x0708)], host, guest);

        // Once it is there, no turn moves it, even should its client report it elsewhere later.
        var arrived = new PcGamePlaySetGhostPos(1, 0x0506, -0x0708, 0x00);
        await guest.SendAsync(arrived);
        Assert.Equal(arrived, await host.ReceiveAsync<PcGamePlaySetGhostPos>(PcGamePlaySetGhostPos.TryRead));
        await HostPassesAsync(host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, host, guest);
        var elsewhere = new PcGamePlaySetGhostPos(1, 0x0809, -0x0A0B, 0x00);
        await guest.SendAsync(elsewhere);
        Assert.Equal(elsewhere, await host.ReceiveAsync<PcGamePlaySetGhostPos>(PcGamePlaySetGhostPos.TryRead));
        await HostPassesAsync(host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostSentWhereItStandsIsNotMoved()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var (x, y) = MatchRules.StartPosition;
        await GuestDiesInHostsShotAsync(host, guest, at: (x, y));
        var aim = new PcGamePlaySetGhostTarget(1, x, y);

        await guest.SendAsync(aim);

        await EveryoneToldAsync(aim, PcGamePlaySetGhostTarget.TryRead, host, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostSentOffAgainIsMovedToWhereItWasSentLast()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var (x, y) = MatchRules.StartPosition;
        await GuestDiesInHostsShotAsync(host, guest, at: (x, y));

        foreach (var aim in new[] { new PcGamePlaySetGhostTarget(1, 0x0102, 0x0304), new PcGamePlaySetGhostTarget(1, -0x0506, 0x0708) })
        {
            await guest.SendAsync(aim);
            await EveryoneToldAsync(aim, PcGamePlaySetGhostTarget.TryRead, host, guest);
        }

        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, [new GhostMove(1, x, y, -0x0506, 0x0708)], host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task OnlyAClientsOwnDeadCharacterHasAGhostToSendOffOrReport()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var (x, y) = MatchRules.StartPosition;

        // The guest's character is alive.
        await guest.SendAsync(new PcGamePlaySetGhostTarget(1, 0x0102, 0x0304));
        await guest.SendAsync(new PcGamePlaySetGhostPos(1, 0x0102, 0x0304, 0x01));
        await QuietAsync(host, guest);

        // The host's character dies, and its ghost is not the guest's.
        await ShotAsync(host, 0, guest);
        await host.SendAsync(new PqGamePlayMakeGhost(0));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
        await guest.SendAsync(new PcGamePlaySetGhostTarget(0, 0x0102, 0x0304));
        await guest.SendAsync(new PcGamePlaySetGhostPos(0, 0x0102, 0x0304, 0x01));
        await QuietAsync(host, guest);

        // No ghost was sent anywhere, so the next turn moves none.
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 1, turnCount: 1, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostCatchingABoxIsPassedOnAsAnyCatch()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        await GuestDiesInHostsShotAsync(host, guest, at: MatchRules.StartPosition);
        var caught = new PcGamePlayEatItemBox(1, CatchOf(1, box: 0x0102, item: 0x0304), TeamGauge: null);

        await guest.SendAsync(caught);

        await EveryoneToldAsync(caught, PcGamePlayEatItemBox.TryRead, host, guest);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AGhostWhosePlayerLeavesTheMatchIsMovedNoMore()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTurnAsync(host, guest);
        var (x, y) = MatchRules.StartPosition;
        await GuestDiesInHostsShotAsync(host, guest, at: (x, y));
        var aim = new PcGamePlaySetGhostTarget(1, 0x0102, 0x0304);
        await guest.SendAsync(aim);
        await EveryoneToldAsync(aim, PcGamePlaySetGhostTarget.TryRead, host, guest);
        await FinishedAsync(0, host, guest);
        await TurnPassedAsync(0, 0, turnCount: 1, [new GhostMove(1, x, y, 0x0102, 0x0304)], host, guest);

        Assert.Equal(LeaveResult.Left, await LeaveAsync(guest));
        Assert.Equal(new PcGamePlayLeaveUser(1), await host.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
        Assert.Equal(
            new PcGameSessionUpdateSlot(SlotChange.Vacated, GameSlotInfo.Open(1), string.Empty),
            await host.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead));

        await host.SendAsync(new PcGamePlayPassTurn(0, 0, 0, 0, 0));
        await TurnPassedAsync(0, 0, turnCount: 1, host);
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task TheStartOfEveryTurnCarriesTheItemBoxesAndDropQualityTheServerIsSetTo()
    {
        var matches = SessionHarness.ShippedMatchOptions();
        matches.ItemBoxesPerTurn = 3;
        matches.ItemDropQuality = 55;
        var services = Services(time: new ManualTime(), matches: matches);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await MissionRoomAsync(host, guest);
        await StartedAsync(host, host, guest);
        await LevelBegunAsync(1, host, guest);

        foreach (var player in new[] { host, guest })
        {
            await player.SendAsync(new PqGameSessionMissionLevelReady(1));
        }

        foreach (var player in new[] { host, guest })
        {
            var start = await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead);
            Assert.Equal<(byte, byte, uint)>((0, 3, 55), (start.TurnSlot, start.ItemBoxBudget, start.DropQuality));
        }

        await HostPassesAsync(host, guest);
        foreach (var player in new[] { host, guest })
        {
            await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead);
            var start = await player.ReceiveAsync<PcGamePlayTurnStart>(PcGamePlayTurnStart.TryRead);
            Assert.Equal<(byte, byte, uint)>((1, 3, 55), (start.TurnSlot, start.ItemBoxBudget, start.DropQuality));
        }

        await QuietAsync(host, guest);
    }

    // A catch by the character in slot, of box, of item.
    private static GameScenarioCommandEatItemBox CatchOf(byte slot, ulong box, ushort item) =>
        new(slot, CommandType: 0x0B, TimingTick: 0x0102, Duration: 0x0304, box, item, Reserved: 0);

    // The guest's character dies in the host's shot, where its client last reported it, and its ghost appears there.
    private static async Task GuestDiesInHostsShotAsync(Player host, Player guest, (int X, int Y) at)
    {
        await ShotAsync(host, 0, guest);
        await guest.SendAsync(new PqGamePlayMakeGhost(1));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, at.X, at.Y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, host, guest);
    }

    // The host passes its turn, and the guest is passed the pass.
    private static async Task HostPassesAsync(Player host, Player guest)
    {
        var pass = new PcGamePlayPassTurn(0, 0x0102, 0x0304, 0x01, 0x05060708);
        await host.SendAsync(pass);
        Assert.Equal(pass, await guest.ReceiveAsync<PcGamePlayPassTurn>(PcGamePlayPassTurn.TryRead));
    }
}
