using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// What a match pays, kept once and told to the players it pays, through real sessions on
/// loopback sockets. Every value is invented.
/// </summary>
/// <remarks>
/// Every character starts the match as it was created: at the first level, with no
/// experience and no gold. The amounts are the shipped ones, written out in
/// <see cref="Paying"/>, unless a test says otherwise. Four players of a match between teams
/// play two against two: the host in slot 0, then the next three in slots 2, 1 and 3.
/// </remarks>
public sealed class RewardFlowTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMatchIsKeptOnceWithEachCharactersPayAndHowItsPartEndedAndEachIsKeptThatWay()
    {
        var clock = new ManualTime();
        var store = new ScriptedCharacterStore();
        var services = Services(time: clock, characters: store);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await using var fourth = await ArriveAsync(services, ChannelId, "Hoang");
        var room = await TeamRoomAsync(host, second, third, fourth);
        var start = await StartedAsync(host, host, second, third, fourth);

        await LeaveAsync(fourth);
        foreach (var player in (Player[])[host, second, third])
        {
            Assert.Equal(new PcGamePlayLeaveUser(3), await player.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
            await player.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        }

        await third.SendAsync(new PcGamePlayGiveUp());
        await QuietAsync(host, second, third);
        await second.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(1, [new SeatOf(host, 0), new SeatOf(third, 1, GaveUp: true), new SeatOf(second, 2, GaveUp: true)], host, second, third);
        await QuietAsync(host, second, third);

        var match = Assert.Single(store.Kept);
        Assert.Equal(
            (room, ChannelId, start.MapId, (byte?)null, (byte)1, clock.GetUtcNow(), clock.GetUtcNow()),
            (match.RoomNumber, match.ChannelId, match.MapId, match.MissionDifficulty, match.WinningTeam, match.Started, match.Ended));
        Assert.Equal(
            [
                new MatchPayment(host.CharacterIds[0], 0, MatchEnding.Played, Won: true, Experience: 60, Gold: 180),
                new MatchPayment(third.CharacterIds[0], 1, MatchEnding.GaveUp, Won: true, Experience: 0, Gold: 0),
                new MatchPayment(second.CharacterIds[0], 2, MatchEnding.GaveUp, Won: false, Experience: 0, Gold: 0),
                new MatchPayment(fourth.CharacterIds[0], 3, MatchEnding.Left, Won: false, Experience: 0, Gold: 0),
            ],
            match.Payments);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await store.CharacterAsync("Kimi")).Info));
        Assert.Equal(new CharacterProgress(1, 0, 0, 1, 0), CharacterProgress.Of((await store.CharacterAsync("Linh")).Info));
        Assert.Equal(new CharacterProgress(1, 0, 0, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Rồng")).Info));
        Assert.Equal(new CharacterProgress(1, 0, 0, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Hoang")).Info));
    }

    [Fact]
    public async Task APlayerWhoLeftCountsTheMatchLostEvenWhenItsTeamWins()
    {
        var store = new ScriptedCharacterStore();
        var services = Services(time: new ManualTime(), characters: store);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var second = await ArriveAsync(services, ChannelId, "Rồng");
        await using var third = await ArriveAsync(services, ChannelId, "Linh");
        await TeamRoomAsync(host, second, third);
        await StartedAsync(host, host, second, third);

        await LeaveAsync(third);
        foreach (var player in (Player[])[host, second])
        {
            Assert.Equal(new PcGamePlayLeaveUser(1), await player.ReceiveAsync<PcGamePlayLeaveUser>(PcGamePlayLeaveUser.TryRead));
            await player.ReceiveAsync<PcGameSessionUpdateSlot>(PcGameSessionUpdateSlot.TryRead);
        }

        await second.SendAsync(new PcGamePlayGiveUp());
        await EndToldAsync(1, [new SeatOf(host, 0), new SeatOf(second, 2, GaveUp: true)], host, second);
        await QuietAsync(host, second);

        Assert.Equal(
            new MatchPayment(third.CharacterIds[0], 1, MatchEnding.Left, Won: false, Experience: 0, Gold: 0),
            Assert.Single(store.Kept).Payments.Single(payment => payment.Slot == 1));
        Assert.Equal(new CharacterProgress(1, 0, 0, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Linh")).Info));
    }

    [Fact]
    public async Task ACharacterPaidPastItsLevelsEndIsShownTheLevelGainedAndEveryoneEnteringTheRoomLaterSeesIt()
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        rewards.TeamMatchExperience = 200;
        var services = Services(rewards: rewards);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        var room = await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await guest.SendAsync(new PcGamePlayGiveUp());

        // A win pays 200 times 1.5: the whole of the first level, and the host is at the second.
        var leveled = new GameResultSlot(
            0, 2, 300, [new ExpResult(1, 0, 0, 300), new ExpResult(2, 300, 300, 791)], 180, 0, 0, 0, 0, [], 0, 0, 0, true, 300, 180);
        foreach (var player in (Player[])[host, guest])
        {
            await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
            Assert.Equal(
                new PcGameSessionEndGame(
                    new GameResult(
                        1,
                        [
                            leveled,
                            GameResultSlot.Empty(1),
                            GameResultSlot.NothingEarned(2, 1),
                            .. Enumerable.Range(3, 5).Select(slot => GameResultSlot.Empty((byte)slot)),
                        ],
                        WinnerPointDelta: 0,
                        LoserPointDelta: 0),
                    EndGameMode: 0),
                await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead));
        }

        Assert.Equal(new PcUpdateExp(0, 2, 300, 300, 791), await host.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(0, 180), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        await PickAsync(host, host, guest, card: 0, slot: 0);
        await PickAsync(guest, host, guest, card: 1, slot: 2);

        await using var later = await ArriveAsync(services, ChannelId, "Linh");
        var kimi = (await SeatedAsync(later, room, host, guest)).Slots[0].Occupant!;

        Assert.Equal((2, 2), (kimi.Level, kimi.Appearance.Field0C));
        await QuietAsync(host, guest);
    }

    [Fact]
    public async Task AConnectionsSecondCharacterIsToldItsPayAsItsSecondAccountsAndAMissionIsKeptWithItsDifficulty()
    {
        var store = new ScriptedCharacterStore();
        var services = Services(time: new ManualTime(), characters: store);
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await MissionRoomAsync(pair);
        await StartedAsync(pair, pair);
        await LevelBegunAsync(1, pair);
        await FirstTurnAsync(1, 0, pair);
        await ShotAsync(pair, 0);
        var (x, y) = MatchRules.StartPosition;

        await pair.SendAsync(new PqGamePlayMakeGhost(0));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(0, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, pair);
        await pair.SendAsync(new PqGamePlayMakeGhost(1));
        await EveryoneToldAsync(new PcGamePlayMakeGhost(1, x, y, MatchRules.PlayersTeam), PcGamePlayMakeGhost.TryRead, pair);
        await FinishedAsync(0, pair);

        // Lost by the deaths of both, each paid the quarter a lost mission pays, the second told as account slot 1.
        await EndToldAsync(MatchRules.MissionsTeam, [pair], pair);
        await QuietAsync(pair);
        var match = Assert.Single(store.Kept);
        Assert.Equal(((ushort)0, (byte?)0, MatchRules.MissionsTeam), (match.MapId, match.MissionDifficulty, match.WinningTeam));
        Assert.Equal(
            [
                new MatchPayment(pair.CharacterIds[0], 0, MatchEnding.Played, Won: false, Experience: 10, Gold: 15),
                new MatchPayment(pair.CharacterIds[1], 1, MatchEnding.Played, Won: false, Experience: 10, Gold: 15),
            ],
            match.Payments);
    }

    [Fact]
    public async Task TheResultIsShownAtOnceAndThePayIsToldOnlyOnceItIsKeptAfterAFailedTry()
    {
        var clock = new ManualTime();
        var store = new ScriptedCharacterStore();
        store.FailNext(KeepFailure.BeforeKeeping);
        var services = Services(time: clock, characters: store);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        await guest.SendAsync(new PcGamePlayGiveUp());
        foreach (var player in (Player[])[host, guest])
        {
            await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
            await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);
        }

        // The wait for the cards, and the wait before the payment is tried again.
        await UntilAsync(() => clock.Pending == 2);
        await QuietAsync(host, guest);
        clock.Advance(SessionHarness.ShippedRewardOptions().PayoutRetryDelay);

        Assert.Equal(new PcUpdateExp(0, 1, 60, 0, 300), await host.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(0, 180), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        Assert.Equal(2, store.Kept.Count);
        await QuietAsync(host, guest);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var waited = 0; !condition(); waited++)
        {
            Assert.True(waited < 1000, "the condition never held");
            await Task.Delay(10, Cancel);
        }
    }
}
