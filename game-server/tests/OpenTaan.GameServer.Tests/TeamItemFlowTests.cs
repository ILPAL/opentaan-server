using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// The items of a match between teams, decided on the server: the boxes it places before
/// every start of a turn, in a shot of its own; the boxes on the field, which a catch takes
/// off; the item of every box a ghost catches and of every bonus, which the server draws;
/// and the one bonus each shot owes its shooter. Through real sessions on loopback sockets.
/// Every value is invented.
/// </summary>
/// <remarks>
/// <para>
/// The host plays slot 0, on the first team, the second player slot 2, on the other, and
/// the third slot 1, on the host's team, which can lose it without losing the match. Once
/// the match is loaded they stand on the ground, at 900, across the map at 100, 900 and 500,
/// and the first turn is the host's.
/// </para>
/// <para>
/// The chance is scripted. A box takes two draws, its place along the strip and its height;
/// a draw the test has not lined up takes the first of what it is among, which puts a box at
/// the strip's left end, 100 up, or gives the table's first item. Every test runs on a clock
/// that moves only when the test moves it, and a packet the server drops is checked by
/// nothing reaching anyone.
/// </para>
/// </remarks>
public sealed class TeamItemFlowTests
{
    private const int Ground = 900;

    private static readonly GameScenarioCommandTeamGauge Gauge = new(0, 0x0C, 0x0102, 0x0304, GaugeType: 3, NewGaugeValue: 0x0015, PreviousGaugeValue: 0x0010);

    [Fact]
    public async Task EveryTurnStartsJustAfterTheBoxesTheServerPlacesForItInAShotOfItsOwn()
    {
        var chance = new ScriptedChance(seed: 0);
        var clock = new ManualTime();
        var services = Services(time: clock, random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");

        // The first turn: a box 100 above each character, at the strip's two ends and above the third.
        var first = await FirstTurnAsync(chance, host, second, third);
        Assert.Equal<(int, int)[]>([(100, 800), (500, 800), (900, 800)], [.. first.Select(box => (box.X, box.Y))]);
        Assert.Equal([801, 201, 801, 201, 801, 201], chance.DrawnAmong);
        Assert.Equal(3, first.Select(box => box.Id).Distinct().Count());

        // After a shot. At 300 the host and the third stand as near, and the host's slot comes
        // first; the other two boxes find no room, every place drawn for them being on a box.
        LineUp(chance, 200, 50);
        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        Assert.Equal((300, Ground - 150), Spot(Assert.Single(await TeamTurnPassedAsync(0, 2, host, second, third))));

        // After a pass, which moves the second player's character to where the pass says.
        LineUp(chance, 300, 100);
        Assert.Equal((400, Ground - 200), Spot(Assert.Single(await TeamPassedAsync(second, 2, 1, host, second, third))));

        // After a turn the server passes, its player having stopped answering: the next is the
        // other team's, whose character stands where its pass put it, nearest to 250.
        LineUp(chance, 150, 0);
        clock.Advance(SessionHarness.ShippedMatchOptions().TurnWait);
        Assert.Equal((250, 0x0304 - 100), Spot(Assert.Single(await TeamTurnPassedAsync(1, 2, host, second, third))));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task EveryReportOfTheServersShotPlayedBackChangesNothingNotEvenAShotBeingPlayed()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third);

        // During the turn, as every client plays the boxes' shot back.
        await FinishedAsync(MatchRules.BoxesShotSlot, host, second, third);
        await QuietAsync(host, second, third);

        // During a shot, late: the shot still waits for its own reports.
        await ShotAsync(host, 0, second, third);
        await FinishedAsync(MatchRules.BoxesShotSlot, host, second, third);
        await QuietAsync(host, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task AGhostsCatchOfABoxOnTheFieldGivesEveryClientTheItemTheServerDrawsOnce()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        var boxes = await FirstTurnAsync(chance, host, second, third);
        await ThirdDiesAsync(host, second, third);

        // The ghost's client drew the first item; the server draws the third.
        LineUp(chance, 2);
        var caught = new PcGamePlayEatItemBox(1, CatchOf(1, boxes[1].Id, item: 0x0A01), TeamGauge: null);
        await third.SendAsync(caught);
        await EveryoneToldAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[1].Id, item: 0x0A03), TeamGauge: null), PcGamePlayEatItemBox.TryRead, host, second, third);
        Assert.Equal(SessionHarness.TestItemDrops.Items.Count, chance.DrawnAmong[^1]);

        // The same box again, and a box that was never on the field.
        await third.SendAsync(caught);
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, 0x0501_0000_0000_0009, item: 0x0A01), TeamGauge: null));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task ABoxIsCaughtByItselfOnlyByAGhostOfTheSendersOwn()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        var boxes = await FirstTurnAsync(chance, host, second, third);
        await ThirdDiesAsync(host, second, third);

        // A character still in play, and another player's ghost.
        await host.SendAsync(new PcGamePlayEatItemBox(0, CatchOf(0, boxes[0].Id, item: 0x0A01), TeamGauge: null));
        await second.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, item: 0x0A01), TeamGauge: null));
        await QuietAsync(host, second, third);

        // The box is still on the field for the ghost's own client.
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, item: 0x0A02), TeamGauge: null));
        await EveryoneToldAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, item: 0x0A01), TeamGauge: null), PcGamePlayEatItemBox.TryRead, host, second, third);
    }

    [Fact]
    public async Task TheBoxesAShotDropsGoOnTheFieldAsManyAsItsTurnAllowed()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third);
        await ThirdDiesAsync(host, second, third);

        // The turn let one box drop; the shot drops two, as the client's ids name them.
        var kept = new GameScenarioCommandMakeItemBox(0, GameScenarioCommandMakeItemBox.Type, 0x0100, 0, 0x0500_0000_0000_0001, 300, 600);
        var extra = new GameScenarioCommandMakeItemBox(0, GameScenarioCommandMakeItemBox.Type, 0x0200, 0, 0x0500_0000_0000_0002, 700, 600);
        var shot = ShotWith(0, kept, extra);
        await host.SendAsync(shot);
        await EveryoneToldAsync(shot, PcGamePlayScenario.TryRead, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);

        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, extra.ItemBoxId, item: 0x0A02), TeamGauge: null));
        await QuietAsync(host, second, third);
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, kept.ItemBoxId, item: 0x0A02), TeamGauge: null));
        await EveryoneToldAsync(new PcGamePlayEatItemBox(1, CatchOf(1, kept.ItemBoxId, item: 0x0A01), TeamGauge: null), PcGamePlayEatItemBox.TryRead, host, second, third);
    }

    [Fact]
    public async Task ABoxAMissileCatchesLeavesTheFieldAndTheShotGoesOnAsItCameEvenWhenItsCatchIsWrong()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        var boxes = await FirstTurnAsync(chance, host, second, third);
        await ThirdDiesAsync(host, second, third);

        // A catch as the shooter's client draws it, one of a box never on the field, and one
        // of an item no box gives.
        var shot = ShotWith(
            0,
            CatchOf(0, boxes[0].Id, item: 0x0A02),
            CatchOf(0, 0x0501_0000_0000_0009, item: 0x0A01),
            CatchOf(0, boxes[2].Id, item: 0x0BAD));
        await host.SendAsync(shot);
        await EveryoneToldAsync(shot, PcGamePlayScenario.TryRead, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);

        // Both boxes the shot named are off the field; the one it missed is not.
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, item: 0x0A02), TeamGauge: null));
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[2].Id, item: 0x0A02), TeamGauge: null));
        await QuietAsync(host, second, third);
        await third.SendAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[1].Id, item: 0x0A02), TeamGauge: null));
        await EveryoneToldAsync(new PcGamePlayEatItemBox(1, CatchOf(1, boxes[1].Id, item: 0x0A01), TeamGauge: null), PcGamePlayEatItemBox.TryRead, host, second, third);
    }

    [Fact]
    public async Task AShooterIsOwedOneBonusForItsShotWithTheItemTheServerDraws()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third);
        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);

        // The host's client asks for it as it plays the turn's end, so it arrives in the next turn.
        LineUp(chance, 1);
        var bonus = BonusOf(0, item: 0x0A03);
        await host.SendAsync(bonus);
        await EveryoneToldAsync(BonusOf(0, item: 0x0A02), PcGamePlayEatBonusItem.TryRead, host, second, third);

        // Once only, and only for the shooter.
        await host.SendAsync(bonus);
        await second.SendAsync(BonusOf(2, item: 0x0A03));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task NoBonusIsOwedForATurnPassedByItsPlayerOrByTheServer()
    {
        var chance = new ScriptedChance(seed: 0);
        var clock = new ManualTime();
        var services = Services(time: clock, random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third);

        await TeamPassedAsync(host, 0, 2, host, second, third);
        await host.SendAsync(BonusOf(0, item: 0x0A01));
        await QuietAsync(host, second, third);

        clock.Advance(SessionHarness.ShippedMatchOptions().TurnWait);
        await TeamTurnPassedAsync(2, 1, host, second, third);
        await second.SendAsync(BonusOf(2, item: 0x0A01));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task ABonusNotAskedForIsOwedNoMoreOnceItsShootersNextTurnStarts()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third);
        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);

        await TeamPassedAsync(second, 2, 1, host, second, third);
        await TeamPassedAsync(third, 1, 2, host, second, third);
        await TeamPassedAsync(second, 2, 0, host, second, third);
        await host.SendAsync(BonusOf(0, item: 0x0A01));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task NoBonusIsOwedInARoomWhoseChancesGiveNone()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await FirstTurnAsync(chance, host, second, third, chanceMode: ChanceMode.Basic);
        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);

        await host.SendAsync(BonusOf(0, item: 0x0A01));
        await QuietAsync(host, second, third);
    }

    [Fact]
    public async Task WithoutItemsACatchAndABonusKeepTheirClientsNoItemAndCarryTheirTeamsGauge()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        var boxes = await FirstTurnAsync(chance, host, second, third, GameMode.NoItems);
        await ThirdDiesAsync(host, second, third);
        var draws = chance.DrawnAmong.Count;

        var caught = new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, ItemDrops.NoItem), Gauge);
        await third.SendAsync(caught);
        await EveryoneToldAsync(caught, PcGamePlayEatItemBox.TryRead, host, second, third);

        // Even a catch naming an item, which no client sends here, and which no client gives.
        var named = new PcGamePlayEatItemBox(1, CatchOf(1, boxes[1].Id, item: 0x0A02), Gauge);
        await third.SendAsync(named);
        await EveryoneToldAsync(named, PcGamePlayEatItemBox.TryRead, host, second, third);
        Assert.Equal(draws, chance.DrawnAmong.Count);

        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);
        draws = chance.DrawnAmong.Count;
        var bonus = new PcGamePlayEatBonusItem(0, CatchOf(0, ulong.MaxValue, ItemDrops.NoItem), Gauge with { GaugeType = 4 });
        await host.SendAsync(bonus);
        await EveryoneToldAsync(bonus, PcGamePlayEatBonusItem.TryRead, host, second, third);
        Assert.Equal(draws, chance.DrawnAmong.Count);
    }

    [Fact]
    public async Task WithItemsACatchOrABonusNamingNoItemStillGivesNone()
    {
        var chance = new ScriptedChance(seed: 0);
        var services = Services(time: new ManualTime(), random: chance);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        var boxes = await FirstTurnAsync(chance, host, second, third);
        await ThirdDiesAsync(host, second, third);
        var draws = chance.DrawnAmong.Count;

        var caught = new PcGamePlayEatItemBox(1, CatchOf(1, boxes[0].Id, ItemDrops.NoItem), TeamGauge: null);
        await third.SendAsync(caught);
        await EveryoneToldAsync(caught, PcGamePlayEatItemBox.TryRead, host, second, third);
        Assert.Equal(draws, chance.DrawnAmong.Count);

        await ShotAsync(host, 0, second, third);
        await FinishedAsync(0, host, second, third);
        await TeamTurnPassedAsync(0, 2, host, second, third);
        draws = chance.DrawnAmong.Count;
        await host.SendAsync(BonusOf(0, ItemDrops.NoItem));
        await EveryoneToldAsync(BonusOf(0, ItemDrops.NoItem), PcGamePlayEatBonusItem.TryRead, host, second, third);
        Assert.Equal(draws, chance.DrawnAmong.Count);
    }

    [Fact]
    public async Task TheFieldHoldsNoMoreBoxesThanTheServerAllowsUntilBoxesAreCaught()
    {
        var chance = new ScriptedChance(seed: 0);
        var matches = SessionHarness.ShippedMatchOptions();
        matches.MostItemBoxes = 4;
        var services = Services(time: new ManualTime(), random: chance, matches: matches);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        Player[] everyone = [host, second, third];

        // Three boxes, with room for the shot's one.
        var boxes = await FirstTurnAsync(chance, host, second, third);

        // The last box the field has room for, and none for the shot, though three free
        // spots are lined up: above the host, where its pass puts it, and 100 up at 400 and
        // 700. The server places the next turn's boxes as it takes the pass.
        LineUp(chance, 0, 0, 142, 0, 442, 0);
        await PassAsync(host, 0, second, third);
        Assert.Single(await TurnEndThenBoxesAsync(0, 2, shotBoxes: 0, everyone));
        chance.Draws.Clear();

        // No room at all, though a free spot is lined up.
        LineUp(chance, 100, 0);
        await PassAsync(second, 2, host, third);
        Assert.Empty(await TurnEndThenBoxesAsync(2, 1, shotBoxes: 0, everyone));

        // A box caught makes room for one more: at the spot drawn for last time.
        var shot = ShotWith(1, CatchOf(1, boxes[0].Id, item: 0x0A01));
        await third.SendAsync(shot);
        await EveryoneToldAsync(shot, PcGamePlayScenario.TryRead, host, second);
        await FinishedAsync(1, everyone);
        Assert.Equal((358, 0x0304 - 100), Spot(Assert.Single(await TurnEndThenBoxesAsync(1, 2, shotBoxes: 0, everyone))));
        await QuietAsync(everyone);
    }

    // Starts a match between teams for the three, which report their characters standing on
    // the ground at 100, 900 and 500 across, with the draws lined up for the first turn's
    // three boxes, 100 above each; returns the boxes, in the order they were placed.
    private static async Task<IReadOnlyList<ItemBox>> FirstTurnAsync(
        ScriptedChance chance,
        Player host,
        Player second,
        Player third,
        GameMode gameMode = GameMode.Items,
        ChanceMode chanceMode = ChanceMode.Bonus)
    {
        await TeamRoomAsync(gameMode, chanceMode, host, second, third);
        await StartedAsync(host, host, second, third);
        LineUp(chance, 0, 0, 400, 0, 800, 0);
        await host.SendAsync(StandingAt(0, 100));
        await second.SendAsync(StandingAt(2, 900));
        await third.SendAsync(StandingAt(1, 500));
        return [.. (await TeamTurnToldAsync(0, host, second, third)).Select(BoxOf)];
    }

    // The third player's character dies, and everyone is told its ghost is where it stood.
    private static async Task ThirdDiesAsync(Player host, Player second, Player third)
    {
        await third.SendAsync(new PqGamePlayMakeGhost(1));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, 500, Ground, 1), PcGamePlayMakeGhost.TryRead, host, second, third);
    }

    // The player passes the turn of the character in slot, and the others are passed the pass.
    private static async Task PassAsync(Player passer, byte slot, params Player[] others)
    {
        var pass = new PcGamePlayPassTurn(slot, 0x0102, 0x0304, 0x01, 0x05060708);
        await passer.SendAsync(pass);
        await EveryoneToldAsync(pass, PcGamePlayPassTurn.TryRead, others);
    }

    // Each player is told the turn in from ends, then the boxes placed for the turn in to, then its start.
    private static async Task<IReadOnlyList<GameScenarioCommandMakeItemBox>> TurnEndThenBoxesAsync(byte from, byte to, byte shotBoxes, Player[] players)
    {
        IReadOnlyList<GameScenarioCommandMakeItemBox> placed = [];
        foreach (var player in players)
        {
            Assert.Equal(MatchRules.TurnEndOf(from), await player.ReceiveAsync<PcGamePlayTurnEnd>(PcGamePlayTurnEnd.TryRead));
            placed = await BoxesThenTurnAsync(to, shotBoxes, ghostMoves: [], player);
        }

        return placed;
    }

    // A shot by the character in slot whose steps are these, then the mark that ends every shot.
    private static PcGamePlayScenario ShotWith(byte slot, params GameScenarioCommand[] steps)
    {
        var shot = Shot(slot);
        var played = shot.Scenario;
        return new PcGamePlayScenario(
            shot.Direction,
            shot.FiringSlot,
            new GameScenario(
                played.SlotIndex,
                played.ActionType,
                [.. steps, new GameScenarioCommand(slot, 0x08, 0x0D0E, 0)],
                played.DelayTimings,
                played.Effects,
                played.WeaponFlags,
                played.InitialAngle,
                played.HitSlots,
                played.Seed),
            shot.TotalStats,
            shot.AmmoAddition,
            shot.Integrity);
    }

    private static PcGamePlayLoadingDone StandingAt(byte slot, int x) => new([new LoadedCharacter(slot, x, Ground)]);

    private static GameScenarioCommandEatItemBox CatchOf(byte slot, ulong box, ushort item) =>
        new(slot, CommandType: 0x0B, TimingTick: 0x0102, Duration: 0x0304, box, item, Reserved: 0);

    // A bonus for the character in slot, as its client sends one with items: a catch of no box.
    private static PcGamePlayEatBonusItem BonusOf(byte slot, ushort item) => new(slot, CatchOf(slot, ulong.MaxValue, item), TeamGauge: null);

    private static (int X, int Y) Spot(GameScenarioCommandMakeItemBox box) => (box.SpawnX, box.SpawnY);

    private static void LineUp(ScriptedChance chance, params int[] draws)
    {
        foreach (var draw in draws)
        {
            chance.Draws.Enqueue(draw);
        }
    }
}
