using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>How shots and gifts reach the result rows and payouts through real connections.</summary>
public sealed class MatchStatisticsFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAcceptedEnemyHitsCountAndAConfirmedKillCreditsItsShooterAndPriorTeammateOnce(bool fell)
    {
        var services = Services(time: new ManualTime());
        await using var first = await ArriveAsync(services, ChannelId, "Kimi", "Linh");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await TeamRoomAsync(first, second);
        await StartedAsync(first, first, second);
        await first.SendAsync(Loading(0, 1));
        await second.SendAsync(Loading(2, 3));
        await TeamTurnToldAsync(0, first, second);

        var opening = ShotWith(0, Damage(2, 250), Damage(2, 275), Damage(0, 800), Damage(1, 800), Damage(100, 800), Death(3));
        await second.SendAsync(opening);
        await QuietAsync(second, first);
        await FireAsync(first, opening, second);
        await first.SendAsync(opening);
        await QuietAsync(first, second);
        await FinishedAsync(0, first, second);
        await TeamTurnPassedAsync(0, 2, first, second);
        await FireAsync(second, ShotWith(2, Damage(1, 400)), first);
        await FinishedAsync(2, first, second);
        await TeamTurnPassedAsync(2, 1, first, second);

        var finisher = ShotWith(1, Damage(2, 900), Death(2));
        await FireAsync(first, finisher, second);
        await first.SendAsync(new PqGamePlayMakeGhost(2));
        await QuietAsync(first, second);
        await second.SendAsync(new PqGamePlayMakeGhost(2));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(2, MatchRules.StartX, MatchRules.StartY, 2), PcGamePlayMakeGhost.TryRead, first, second);
        await second.SendAsync(new PqGamePlayMakeGhost(2));
        await QuietAsync(second, first);
        await FinishedAsync(1, first, second);
        await TeamTurnPassedAsync(1, 3, first, second);
        await TeamPassedAsync(second, 3, 0, first, second);

        // A dead target, wrong action, and a damage value with the sign bit set count as a miss.
        // A fall or a ghost report matching only an earlier shot earns no kill.
        GameScenarioCommand[] miss = [Damage(2, 800), Damage(3, 800, type: 0), Damage(3, uint.MaxValue)];
        if (fell)
        {
            await FireAsync(first, ShotWith(0, [.. miss, Death(3)]), second);
            var fall = new PcGamePlayDeathByDrop(3, 0x0506, 0x0708);
            await second.SendAsync(fall);
            await EveryoneToldAsync(fall, PcGamePlayDeathByDrop.TryRead, first);
        }
        else
        {
            await FireAsync(first, ShotWith(0, miss), second);
            await second.SendAsync(new PqGamePlayMakeGhost(3));
        }

        await EveryoneToldAsync(new PcGamePlayMakeGhost(3, 0x0102, 0x0304, 2), PcGamePlayMakeGhost.TryRead, first, second);
        await FinishedAsync(0, first, second);

        var (_, result) = await ResultToldAsync(first, second);
        Assert.Equal(1, result.WinningTeam);
        Assert.Equal(((byte)0, 525u, 0.5f, 100u), StatsOf(result.Slots[0]));
        Assert.Equal(((byte)1, 900u, 1f, 0u), StatsOf(result.Slots[1]));
        Assert.Equal(((byte)0, 400u, 1f, 0u), StatsOf(result.Slots[2]));
        Assert.Equal(((byte)0, 0u, 0f, 0u), StatsOf(result.Slots[3]));
        Assert.Equal((67u, 199u), (result.Slots[0].ExpReward, result.Slots[0].GoldEarned));
        Assert.Equal((211u, 403u), (result.Slots[1].ExpReward, result.Slots[1].GoldEarned));
        Assert.Equal(new ResultBonus(4, 100, 100), Assert.Single(result.Slots[1].Bonuses));
        Assert.All(result.Slots.Where(row => row.SlotIndex != 1), row => Assert.Empty(row.Bonuses));
        Assert.Equal((44u, 130u), (result.Slots[2].ExpReward, result.Slots[2].GoldEarned));
        await PayToldAsync(first, 0, 67, 199);
        await PayToldAsync(first, 1, 211, 403);
        await PayToldAsync(second, 0, 44, 130);
        await PayToldAsync(second, 1, 40, 120);
        await QuietAsync(first, second);
    }

    [Fact]
    public async Task ACreditedAssistBreaksTheMvpTieAfterEqualDamageAndKills()
    {
        var services = Services(time: new ManualTime());
        await using var first = await ArriveAsync(services, ChannelId, "Kimi", "Linh");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await TeamRoomAsync(first, second);
        await StartedAsync(first, first, second);
        await first.SendAsync(Loading(0, 1));
        await second.SendAsync(Loading(2, 3));
        await TeamTurnToldAsync(0, first, second);

        await FireAsync(first, ShotWith(0, Damage(2, 100)), second);
        await FinishedAsync(0, first, second);
        await TeamTurnPassedAsync(0, 2, first, second);
        await FireAsync(second, ShotWith(2, Damage(0, 100)), first);
        await FinishedAsync(2, first, second);
        await TeamTurnPassedAsync(2, 1, first, second);

        await FireAsync(first, ShotWith(1, Damage(2, 1), Death(2), Damage(3, 1), Death(3)), second);
        await GhostToldAsync(second, 2, team: 2, first, second);
        await GhostToldAsync(second, 3, team: 2, first, second);
        await FinishedAsync(1, first, second);

        var (_, result) = await ResultToldAsync(first, second);
        Assert.Equal(((byte)0, 100u, 1f, 100u), StatsOf(result.Slots[0]));
        Assert.Equal(((byte)0, 100u, 1f, 0u), StatsOf(result.Slots[2]));
        Assert.Equal(new ResultBonus(4, 100, 100), Assert.Single(result.Slots[0].Bonuses));
        Assert.All(result.Slots.Skip(1), row => Assert.Empty(row.Bonuses));
        await PayToldAsync(first, 0, 161, 283);
        await PayToldAsync(first, 1, 135, 360);
        await PayToldAsync(second, 0, 41, 122);
        await PayToldAsync(second, 1, 40, 120);
        await QuietAsync(first, second);
    }

    [Fact]
    public async Task AShotEarningForceAndKillMedalsAlsoRaisesTheCharactersCardOfferAndKeptPay()
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        rewards.CardGoldWeight = 1;
        rewards.CardExperienceWeight = 0;
        rewards.CardMinimumPercent = 40;
        rewards.CardMaximumPercent = 40;
        var store = new ScriptedCharacterStore();
        var services = Services(time: new ManualTime(), rewards: rewards, characters: store);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var enemies = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await using var allies = await ArriveAsync(services, ChannelId, "Linh", "Nhung");
        await using var enemy = await ArriveAsync(services, ChannelId, "Dung");
        Player[] everyone = [host, enemies, allies, enemy];
        await TeamRoomAsync(host, enemies, allies, enemy);
        await StartedAsync(host, everyone);
        await host.SendAsync(Loading(0));
        await enemies.SendAsync(Loading(2, 3));
        await allies.SendAsync(Loading(1, 4));
        await enemy.SendAsync(Loading(6));
        await TeamTurnToldAsync(0, everyone);

        await FireAsync(host, ShotWith(0, Damage(2, 1000), Death(2), Damage(3, 1000), Death(3), Damage(6, 1000), Death(6)), enemies, allies, enemy);
        await GhostToldAsync(enemies, 2, team: 2, everyone);
        await GhostToldAsync(enemies, 3, team: 2, everyone);
        await GhostToldAsync(enemy, 6, team: 2, everyone);
        await FinishedAsync(0, everyone);

        var (offers, result) = await ResultToldAsync(everyone);
        var hero = result.Slots[0];
        Assert.Equal(((byte)3, 3000u, 1f, 0u), StatsOf(hero));
        Assert.Equal(((byte)1, (byte)1, (byte)0), (hero.ForceMedalAward, hero.KillMedalAward, hero.SupportMedalAward));
        Assert.Equal(new ResultBonus(4, 100, 100), Assert.Single(hero.Bonuses));
        Assert.Equal((317u, 662u, 662u), (hero.ExpReward, hero.GoldEarned, hero.GoldReward));
        Assert.Equal(new ExpResult(2, 317, 300, 791), hero.Levels[^1]);
        Assert.Equal(new GameRewardGold("Gold", host.CharacterIds[0], 224, false, 0), offers.Rewards[0]);
        Assert.Equal(new PcUpdateExp(0, 2, 317, 300, 791), await host.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(0, 662), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        await PayToldAsync(enemies, 0, 40, 120);
        await PayToldAsync(enemies, 1, 40, 120);
        await PayToldAsync(allies, 0, 60, 180);
        await PayToldAsync(allies, 1, 60, 180);
        await PayToldAsync(enemy, 0, 40, 120);

        await host.SendAsync(new PqResultSelectCard(7, 0));
        await EveryoneToldAsync(new PrResultSelectCard(7, 0), PrResultSelectCard.TryRead, everyone);
        Assert.Equal(PcUpdateMoney.GoldOf(0, 886), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        var saved = (await store.CharacterAsync("Kimi")).Info;
        Assert.Equal((317u, 886u), (saved.CurrentExperience, saved.Gold));
        Assert.Equal((1u, 1u, 0u), (saved.ForceMedals, saved.KillMedals, saved.SupportMedals));
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task ARealCaptainOrGhostGiftAddsSupportOnceForEachItemSlotInThatTurn()
    {
        var services = Services(time: new ManualTime(), random: new ScriptedChance(0));
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var enemy = await ArriveAsync(services, ChannelId, "Rồng");
        await using var ally = await ArriveAsync(services, ChannelId, "Linh");
        Player[] everyone = [host, enemy, ally];
        await TeamRoomAsync(GameMode.CaptainItems, host, enemy, ally);
        var start = await StartedAsync(host, everyone);
        Assert.Equal(0, start.CaptainSlots[1]);
        await host.SendAsync(Loading(0));
        await enemy.SendAsync(Loading(2));
        await ally.SendAsync(Loading(1));
        await TeamTurnToldAsync(0, everyone);

        var captainGift = new PcGamePlayUseItem(1, 1, GhostGift: 0, CaptainGift: 1, ItemSlot: 0, ItemId: 0x0A01);
        var ghostGift = captainGift with { GhostGift = 1, CaptainGift = 0 };
        await ItemRelayedAsync(ally, ghostGift, everyone); // The giver is still alive.
        await ItemRelayedAsync(ally, new PcGamePlayUseItem(1, 2, GhostGift: 0, CaptainGift: 1, ItemSlot: 0, ItemId: 0x0A01), everyone);
        await ItemRelayedAsync(ally, captainGift with { ItemSlot = 0xFF }, everyone);
        await ItemRelayedAsync(ally, captainGift with { GhostGift = 1 }, everyone);
        await ItemRelayedAsync(ally, captainGift with { ItemId = 0 }, everyone);
        await ItemRelayedAsync(ally, captainGift with { ItemId = ushort.MaxValue }, everyone);
        for (byte item = 0; item < 3; item++)
        {
            await ItemRelayedAsync(ally, captainGift with { ItemSlot = item }, everyone);
        }

        await ItemRelayedAsync(ally, captainGift with { ItemId = 0x0A02 }, everyone);
        await TeamPassedAsync(host, 0, 2, everyone);
        await FireAsync(enemy, ShotWith(2, Damage(1, 1000), Death(1)), host, ally);
        await GhostToldAsync(ally, 1, team: 1, everyone);
        await FinishedAsync(2, everyone);
        await TeamTurnPassedAsync(2, 0, everyone);

        // Three items on each of the next two turns and one on the last bring the total to 500.
        for (var turn = 0; turn < 2; turn++)
        {
            await ItemRelayedAsync(ally, captainGift, everyone); // The giver is now a ghost.
            for (byte item = 0; item < 3; item++)
            {
                await ItemRelayedAsync(ally, ghostGift with { ItemSlot = item }, everyone);
            }

            await ItemRelayedAsync(ally, ghostGift, everyone);
            await TeamPassedAsync(host, 0, 2, everyone);
            await ItemRelayedAsync(ally, ghostGift, everyone); // The current turn is the other team's.
            await TeamPassedAsync(enemy, 2, 0, everyone);
        }

        await ItemRelayedAsync(ally, ghostGift, everyone);
        await FireAsync(host, ShotWith(0), enemy, ally);
        await ItemRelayedAsync(ally, ghostGift with { ItemSlot = 1 }, everyone); // The captain has already fired.
        await FinishedAsync(0, everyone);
        await TeamTurnPassedAsync(0, 2, everyone);
        await enemy.SendAsync(new PcGamePlayGiveUp());

        var (_, result) = await ResultToldAsync(everyone);
        var helper = result.Slots[1];
        Assert.Equal(((byte)0, 0u, 0f, 500u), StatsOf(helper));
        Assert.Equal(((byte)0, (byte)0, (byte)1), (helper.ForceMedalAward, helper.KillMedalAward, helper.SupportMedalAward));
        Assert.Equal((60u, 180u), (helper.ExpReward, helper.GoldEarned));
        Assert.All(result.Slots, row => Assert.Empty(row.Bonuses));
        await PayToldAsync(host, 0, 60, 180);
        await PayToldAsync(ally, 0, 60, 180);
        await QuietAsync(everyone);
    }

    [Fact]
    public async Task MissionEnemySlotsCanBeKilledAgainOnTheNextLevelAndStatsDoNotRaiseMissionPayOrAwardMedals()
    {
        var rooms = SessionHarness.ShippedRoomOptions();
        rooms.Missions = new Dictionary<ushort, byte> { [0] = 2 };
        var services = Services(rooms: rooms, time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(host);
        await StartedAsync(host, host);
        await LevelBegunAsync(1, host);
        await FirstTurnAsync(1, 0, host);

        await FireAsync(host, ShotWith(0, Damage(100, 600), Death(100), Death(100), Damage(100, 600), Damage(101, 900), Death(101, type: 7)));
        await host.SendAsync(Asking(2));
        await FinishedAsync(0, host);
        await EveryoneToldAsync(new PcGameSessionStartMissionLevel(2), PcGameSessionStartMissionLevel.TryRead, host);
        await FirstTurnAsync(2, 0, host);
        await FireAsync(host, ShotWith(0, Damage(100, 1500), Death(100), Damage(0, 500)));
        await host.SendAsync(Asking(PqGameSessionStartMissionLevel.NoNextLevel));
        await FinishedAsync(0, host);

        var (_, result) = await ResultToldAsync(host);
        var row = result.Slots[0];
        Assert.Equal(((byte)3, 3000u, 1f, 0u), StatsOf(row));
        Assert.Equal(((byte)0, (byte)0, (byte)0), (row.ForceMedalAward, row.KillMedalAward, row.SupportMedalAward));
        Assert.Equal((40u, 60u), (row.ExpReward, row.GoldEarned));
        Assert.Empty(row.Bonuses);
        await PayToldAsync(host, 0, 40, 60);
        await QuietAsync(host);
    }

    [Fact]
    public async Task ACharacterWhoGivesUpStillShowsItsStatsWithoutPayOrMedals()
    {
        var services = Services(time: new ManualTime());
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await AtFirstTeamTurnAsync(host, guest);
        await FireAsync(host, ShotWith(0, Damage(2, 3250)), guest);
        await FinishedAsync(0, host, guest);
        await TeamTurnPassedAsync(0, 2, host, guest);
        await host.SendAsync(new PcGamePlayGiveUp());

        var (_, result) = await ResultToldAsync(host, guest);
        var row = result.Slots[0];
        Assert.Equal(((byte)0, 3250u, 1f, 0u), StatsOf(row));
        Assert.Equal(((byte)0, (byte)0, (byte)0), (row.ForceMedalAward, row.KillMedalAward, row.SupportMedalAward));
        Assert.Equal((false, 0u, 0u), (row.HasReward, row.ExpReward, row.GoldEarned));
        Assert.All(result.Slots, item => Assert.Empty(item.Bonuses));
        await PayToldAsync(guest, 0, 60, 180);
        await QuietAsync(host, guest);
    }

    private static GameScenarioCommandDamage Damage(byte target, uint damage, byte type = 4) =>
        new(target, type, TimingTick: 100, Duration: 0, damage, TargetHpBeforeHit: 1000, DelayValue: 0, StaggerValue: 0);

    private static GameScenarioCommand Death(byte target, byte type = 5) => new(target, type, TimingTick: 100, Duration: 0);

    private static PcGamePlayScenario ShotWith(byte slot, params GameScenarioCommand[] steps) =>
        new(1, slot, new GameScenario(slot, 2, [.. steps, new GameScenarioCommand(slot, 8, 2000, 0)], [], [], 0, 0, [], 0), 0, 0, Report);

    private static async Task FireAsync(Player shooter, PcGamePlayScenario shot, params Player[] others)
    {
        await shooter.SendAsync(shot);
        await EveryoneToldAsync(shot, PcGamePlayScenario.TryRead, others);
    }

    private static async Task GhostToldAsync(Player victim, byte slot, byte team, params Player[] everyone)
    {
        await victim.SendAsync(new PqGamePlayMakeGhost(slot));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(slot, MatchRules.StartX, MatchRules.StartY, team), PcGamePlayMakeGhost.TryRead, everyone);
    }

    private static async Task ItemRelayedAsync(Player giver, PcGamePlayUseItem item, Player[] everyone)
    {
        await giver.SendAsync(item);
        await EveryoneToldAsync(item, PcGamePlayUseItem.TryRead, everyone);
    }

    private static async Task<(PcResultSetReward Offers, GameResult Result)> ResultToldAsync(params Player[] players)
    {
        var first = players[0];
        var offers = await first.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        var end = await first.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);
        foreach (var player in players.Skip(1))
        {
            Assert.Equal(offers, await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead));
            Assert.Equal(end, await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead));
        }

        return (offers, end.Result);
    }

    private static (byte Kills, uint Damage, float HitRate, uint Support) StatsOf(GameResultSlot row) =>
        (row.KillStat, row.ForceStat, row.HitRate, row.SupportStat);

    private static async Task PayToldAsync(Player player, byte accountSlot, uint experience, uint gold)
    {
        Assert.Equal(new PcUpdateExp(accountSlot, 1, experience, 0, 300), await player.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(accountSlot, gold), await player.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
    }
}
