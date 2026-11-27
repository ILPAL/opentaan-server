using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>Keeping what each match paid away from the rooms' lock: told once, tried again, given up on.</summary>
public sealed class MatchPayoutsTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APaymentKeptAtTheFirstTryIsToldOnceWithHowTheCharacterStands()
    {
        var (store, clock, payouts, kimi) = await PaidAsync();
        var told = new ConcurrentBag<IReadOnlyDictionary<uint, CharacterProgress>>();

        payouts.Pay(MatchPaying(kimi), told.Add);
        await payouts.WhenPaidAsync().WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), Assert.Single(told)[kimi]);
        Assert.Single(store.Kept);
        Assert.Equal(0, clock.Pending);
    }

    [Fact]
    public async Task ATryWhoseAnswerWasLostIsMadeAgainAfterTheDelayAndPaysNoOneTwice()
    {
        var (store, clock, payouts, kimi) = await PaidAsync();
        store.FailNext(KeepFailure.AfterKeeping, KeepFailure.BeforeKeeping);
        var told = new ConcurrentBag<IReadOnlyDictionary<uint, CharacterProgress>>();

        payouts.Pay(MatchPaying(kimi), told.Add);
        await RetryWaitingAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(2) - TimeSpan.FromMilliseconds(1));
        Assert.Single(store.Kept);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await RetryWaitingAsync(clock);
        Assert.Empty(told);
        clock.Advance(TimeSpan.FromSeconds(2));
        await payouts.WhenPaidAsync().WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        Assert.Equal(3, store.Kept.Count);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), Assert.Single(told)[kimi]);
        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await store.CharacterAsync("Kimi")).Info));
    }

    [Fact]
    public async Task APaymentFailingEveryTryIsGivenUpAfterTheLastAndToldToNoOne()
    {
        var (store, clock, payouts, kimi) = await PaidAsync();
        store.FailNext(KeepFailure.BeforeKeeping, KeepFailure.BeforeKeeping, KeepFailure.BeforeKeeping);
        var told = new ConcurrentBag<IReadOnlyDictionary<uint, CharacterProgress>>();

        payouts.Pay(MatchPaying(kimi), told.Add);
        await RetryWaitingAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        await RetryWaitingAsync(clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        await payouts.WhenPaidAsync().WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        Assert.Equal(3, store.Kept.Count);
        Assert.Empty(told);
        Assert.Equal(0, clock.Pending);
        Assert.Equal(new CharacterProgress(1, 0, 0, 0, 0), CharacterProgress.Of((await store.CharacterAsync("Kimi")).Info));
    }

    [Fact]
    public async Task StoppingWaitsForEveryPaymentUnderWay()
    {
        var (store, clock, payouts, kimi) = await PaidAsync();
        store.FailNext(KeepFailure.BeforeKeeping);

        payouts.Pay(MatchPaying(kimi), _ => { });
        await RetryWaitingAsync(clock);
        var stopping = payouts.StopAsync(Cancel);
        Assert.False(stopping.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(2));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        Assert.Equal(new CharacterProgress(1, 60, 180, 1, 0), CharacterProgress.Of((await store.CharacterAsync("Kimi")).Info));
    }

    // A store holding one new character, Kimi, the clock the payouts wait on, and the payouts.
    private static async Task<(ScriptedCharacterStore Store, ManualTime Clock, MatchPayouts Payouts, uint Kimi)> PaidAsync()
    {
        var store = new ScriptedCharacterStore();
        var kimi = (await store.CreateAsync(
            1001,
            "Kimi",
            id => new Character(
                1001,
                new CharacterInfo(id, "Kimi", 1, 0, 0, 300, 0, Gender.Male, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
                Character.AppearanceOf(Gender.Male, id, 1, new AppearanceSlot[AppearanceSlot.Count])),
            Cancel)).Character!.CharacterId;
        var clock = new ManualTime();
        var payouts = new MatchPayouts(store, Options.Create(SessionHarness.ShippedRewardOptions()), clock, NullLogger<MatchPayouts>.Instance);
        return (store, clock, payouts, kimi);
    }

    // A match between teams Kimi won, paying it a win.
    private static MatchRecord MatchPaying(uint kimi) => new(
        Guid.NewGuid(),
        RoomNumber: 1,
        ChannelId: 11,
        MapId: 3,
        MissionDifficulty: null,
        WinningTeam: 1,
        Started: DateTimeOffset.UnixEpoch,
        Ended: DateTimeOffset.UnixEpoch,
        [new MatchPayment(kimi, 0, MatchEnding.Played, Won: true, Experience: 60, Gold: 180)]);

    // Waits for the payment to wait on the clock for its next try.
    private static async Task RetryWaitingAsync(ManualTime clock)
    {
        for (var waited = 0; clock.Pending == 0; waited++)
        {
            Assert.True(waited < 1000, "the payment never waited to try again");
            await Task.Delay(10, Cancel);
        }
    }
}
