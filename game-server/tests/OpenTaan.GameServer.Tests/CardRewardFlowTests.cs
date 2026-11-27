using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using static OpenTaan.Tests.GameServer.Matching;
using static OpenTaan.Tests.GameServer.Playing;

namespace OpenTaan.Tests.GameServer;

/// <summary>A result card's reward, from its offer through a real connection to its kept balance.</summary>
public sealed class CardRewardFlowTests
{
    [Fact]
    public async Task TheCardACharacterChoosesPaysItsOwnOfferOnlyAfterThePickIsKept()
    {
        var store = new ScriptedCharacterStore();
        var services = Services(rewards: GoldOnly(), characters: store);
        await using var host = await ArriveAsync(services, ChannelId, "Kimi");
        await using var guest = await ArriveAsync(services, ChannelId, "Rồng");
        await TeamRoomAsync(host, guest);
        await StartedAsync(host, host, guest);

        // The guest stays through the result after giving up. It earned no match pay,
        // but can still pick a card worth the gold floor.
        await guest.SendAsync(new PcGamePlayGiveUp());
        var offered = await host.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        Assert.Equal(offered, await guest.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead));
        Assert.Equal(8, offered.Rewards.Count);
        Assert.Equal(new GameRewardGold("Gold", host.CharacterIds[0], 72, false, 0), offered.Rewards[0]);
        Assert.Equal(new GameRewardGold("Gold", guest.CharacterIds[0], 50, false, 0), offered.Rewards[2]);
        Assert.All(offered.Rewards.Where(reward => reward.CharacterId == 0), reward =>
            Assert.Equal(new GameRewardGold("Empty", 0, 0, false, 0), reward));
        await host.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);
        await guest.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);

        // The match itself pays first. The card's position, 5, is unrelated to the
        // host's slot, 0, or its offer's position in the reward vector.
        Assert.Equal(new PcUpdateExp(0, 1, 60, 0, 300), await host.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(0, 180), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        await host.SendAsync(new PqResultSelectCard(5, 0));
        await EveryoneToldAsync(new PrResultSelectCard(5, 0), PrResultSelectCard.TryRead, host, guest);
        Assert.Equal(PcUpdateMoney.GoldOf(0, 252), await host.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));

        // A contested manual pick changes nothing and is refused only to its picker.
        await guest.SendAsync(new PqResultSelectCard(5, 2));
        Assert.Equal(new PrResultAlreadyOpend(5, 2), await guest.ReceiveAsync<PrResultAlreadyOpend>(PrResultAlreadyOpend.TryRead));
        await QuietAsync(host, guest);

        // The client's timed choice of a card left earns the same offer.
        await guest.SendAsync(new PqResultRandSelect(2));
        var chosen = await guest.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead);
        Assert.Equal((byte)2, chosen.GameSlotIndex);
        Assert.NotEqual((byte)5, chosen.CardIndex);
        Assert.Equal(chosen, await host.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead));
        Assert.Equal(PcUpdateMoney.GoldOf(0, 50), await guest.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        await QuietAsync(host, guest);

        Assert.Equal(new CharacterProgress(1, 60, 252, 1, 0), CharacterProgress.Of((await store.CharacterAsync("Kimi")).Info));
        Assert.Equal(new CharacterProgress(1, 0, 50, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Rồng")).Info));
    }

    [Fact]
    public async Task ATimedExperienceCardUpdatesTheSecondAccountSlotAndNoMatchRecord()
    {
        var store = new ScriptedCharacterStore();
        var services = Services(rewards: ExperienceOnly(), characters: store);
        await using var pair = await ArriveAsync(services, ChannelId, "Rồng", "Hoang");
        await MissionRoomAsync(pair);
        await StartedAsync(pair, pair);

        await pair.SendAsync(new PcGamePlayGiveUp());
        var offered = await pair.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        Assert.Equal(new GameRewardExp("Exp", pair.CharacterIds[0], 20), offered.Rewards[0]);
        Assert.Equal(new GameRewardExp("Exp", pair.CharacterIds[1], 20), offered.Rewards[1]);
        await pair.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);

        await pair.SendAsync(new PqResultRandSelect(1));
        var chosen = await pair.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead);
        Assert.Equal((byte)1, chosen.GameSlotIndex);
        Assert.InRange(chosen.CardIndex, (byte)0, (byte)7);
        Assert.Equal(new PcUpdateExp(1, 1, 20, 0, 300), await pair.ReceiveAsync<PcUpdateExp>(PcUpdateExp.TryRead));
        await QuietAsync(pair);

        Assert.Equal(new CharacterProgress(1, 0, 0, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Rồng")).Info));
        Assert.Equal(new CharacterProgress(1, 20, 0, 0, 1), CharacterProgress.Of((await store.CharacterAsync("Hoang")).Info));
    }

    [Fact]
    public async Task ACardIsNotRevealedWhileItsPaymentWaitsForTheStore()
    {
        var store = new ScriptedCharacterStore();
        var services = Services(rewards: GoldOnly(), characters: store);
        await using var player = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(player);
        await StartedAsync(player, player);
        await player.SendAsync(new PcGamePlayGiveUp());
        await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);
        await AfterMatchAsync(store, "Kimi");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.CardGate = gate;
        try
        {
            await player.SendAsync(new PqResultSelectCard(4, 0));
            var reveal = player.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead);
            await UntilAsync(() => store.CardsKept.Count == 1);
            Assert.False(reveal.IsCompleted);
            Assert.Equal(0u, (await store.CharacterAsync("Kimi")).Info.Gold);

            gate.SetResult();
            Assert.Equal(new PrResultSelectCard(4, 0), await reveal);
            Assert.Equal(PcUpdateMoney.GoldOf(0, 50), await player.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
            await QuietAsync(player);
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task ACardPickedBeforeItsMatchWasKeptWaitsForTheBasePayment()
    {
        var clock = new ManualTime();
        var store = new ScriptedCharacterStore();
        store.FailNext(KeepFailure.BeforeKeeping);
        var services = Services(time: clock, rewards: GoldOnly(), characters: store);
        await using var player = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(player);
        await StartedAsync(player, player);
        await player.SendAsync(new PcGamePlayGiveUp());
        await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);
        await UntilAsync(() => clock.Pending == 2);

        await player.SendAsync(new PqResultSelectCard(4, 0));
        var reveal = player.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.False(reveal.IsCompleted);
        Assert.Empty(store.CardsKept);

        clock.Advance(SessionHarness.ShippedRewardOptions().PayoutRetryDelay);
        Assert.Equal(new PrResultSelectCard(4, 0), await reveal);
        Assert.Equal(PcUpdateMoney.GoldOf(0, 50), await player.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        Assert.Equal(2, store.Kept.Count);
        Assert.Single(store.CardsKept);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingBeforePickingForfeitsTheCardButLeavingAfterItKeepsThePay(bool pickFirst)
    {
        var store = new ScriptedCharacterStore();
        var services = Services(rewards: GoldOnly(), characters: store);
        await using var player = await ArriveAsync(services, ChannelId, "Kimi");
        await MissionRoomAsync(player);
        await StartedAsync(player, player);
        await player.SendAsync(new PcGamePlayGiveUp());
        var offered = await player.ReceiveAsync<PcResultSetReward>(PcResultSetReward.TryRead);
        Assert.Equal(new GameRewardGold("Gold", player.CharacterIds[0], 50, false, 0), offered.Rewards[0]);
        await player.ReceiveAsync<PcGameSessionEndGame>(PcGameSessionEndGame.TryRead);

        if (pickFirst)
        {
            await player.SendAsync(new PqResultSelectCard(7, 0));
            Assert.Equal(new PrResultSelectCard(7, 0), await player.ReceiveAsync<PrResultSelectCard>(PrResultSelectCard.TryRead));
            Assert.Equal(PcUpdateMoney.GoldOf(0, 50), await player.ReceiveAsync<PcUpdateMoney>(PcUpdateMoney.TryRead));
        }

        await LeaveAsync(player);
        var progress = await AfterMatchAsync(store, "Kimi");
        Assert.Equal(
            new CharacterProgress(1, 0, pickFirst ? 50u : 0u, 0, 1),
            progress);
    }

    private static RewardOptions GoldOnly()
    {
        var options = SessionHarness.ShippedRewardOptions();
        options.CardGoldWeight = 1;
        options.CardExperienceWeight = 0;
        options.CardMinimumPercent = 40;
        options.CardMaximumPercent = 40;
        return options;
    }

    private static RewardOptions ExperienceOnly()
    {
        var options = SessionHarness.ShippedRewardOptions();
        options.CardGoldWeight = 0;
        options.CardExperienceWeight = 1;
        options.CardMinimumPercent = 40;
        options.CardMaximumPercent = 40;
        return options;
    }

    private static async Task<CharacterProgress> AfterMatchAsync(ScriptedCharacterStore store, string name)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var progress = CharacterProgress.Of((await store.CharacterAsync(name)).Info);
            if (progress.Losses == 1)
            {
                return progress;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new Xunit.Sdk.XunitException("The match payment was never kept.");
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new Xunit.Sdk.XunitException("The card payment was never attempted.");
    }
}
