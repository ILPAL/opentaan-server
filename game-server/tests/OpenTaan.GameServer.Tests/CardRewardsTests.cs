using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.Tests.GameServer;

/// <summary>What each character's card shows, independently of the card position picked.</summary>
public sealed class CardRewardsTests
{
    [Fact]
    public void ACardIsFixedForItsCharacterAndEmptySlotsHaveRealInertRewards()
    {
        uint?[] characters = [1001, null, null, null, null, 2002, null, null];
        MatchPayment[] payments =
        [
            new(1001, 0, MatchEnding.Played, false, 40, 120),
            new(2002, 5, MatchEnding.Played, true, 60, 180),
        ];
        var options = SessionHarness.ShippedRewardOptions();
        options.CardGoldWeight = 1;
        options.CardExperienceWeight = 0;

        var offers = CardRewards.Roll(characters, payments, options, new Random(123));
        var packet = CardRewards.PacketOf(characters, [offers[1], offers[0]]);

        Assert.Equal(2, offers.Length);
        Assert.Equal((1001u, (byte)0), (offers[0].CharacterId, offers[0].Slot));
        Assert.Equal((2002u, (byte)5), (offers[1].CharacterId, offers[1].Slot));
        Assert.Equal(8, packet.Rewards.Count);
        Assert.All(packet.Rewards, reward => Assert.NotNull(reward));
        var first = Assert.IsType<GameRewardGold>(packet.Rewards[0]);
        var sixth = Assert.IsType<GameRewardGold>(packet.Rewards[5]);
        Assert.InRange(first.Amount, 50u, 144u);
        Assert.InRange(sixth.Amount, 72u, 216u);
        Assert.Equal(1001u, first.CharacterId);
        Assert.Equal(2002u, sixth.CharacterId);
        Assert.False(first.HasPcRoomBonusCard);
        Assert.False(sixth.HasPcRoomBonusCard);
        Assert.Equal(6, packet.Rewards.Count(reward => reward.CharacterId == 0));
        Assert.All(packet.Rewards.Where(reward => reward.CharacterId == 0), reward =>
        {
            var empty = Assert.IsType<GameRewardGold>(reward);
            Assert.Equal(0u, empty.Amount);
            Assert.Equal("Empty", empty.Name);
        });
    }

    [Theory]
    [InlineData(true, 50u)]
    [InlineData(false, 20u)]
    public void ACharacterWhoseMatchPaidNothingMayStillPickTheCardFloor(bool gold, uint floor)
    {
        uint?[] characters = [null, 1001, null, null, null, null, null, null];
        MatchPayment[] payments = [new(1001, 1, MatchEnding.GaveUp, false, 0, 0)];
        var options = SessionHarness.ShippedRewardOptions();
        options.CardGoldWeight = gold ? 1 : 0;
        options.CardExperienceWeight = gold ? 0 : 1;

        var offer = Assert.Single(CardRewards.Roll(characters, payments, options, new Random(7)));
        var reward = CardRewards.PacketOf(characters, [offer]).Rewards[1];

        Assert.Equal(floor, offer.Amount);
        Assert.Equal(gold ? CardRewardKind.Gold : CardRewardKind.Experience, offer.Kind);
        Assert.Equal(gold ? (byte)4 : (byte)3, reward.RewardType);
    }

    [Fact]
    public void AnExperienceCardMayPayMoreThanTheMatchsThreeHundredExperience()
    {
        uint?[] characters = [1001, null, null, null, null, null, null, null];
        MatchPayment[] payments = [new(1001, 0, MatchEnding.Played, true, 300, 0)];
        var options = SessionHarness.ShippedRewardOptions();
        options.CardGoldWeight = 0;
        options.CardExperienceWeight = 1;
        var random = new Random(4);

        var amounts = Enumerable.Range(0, 100)
            .Select(_ => Assert.Single(CardRewards.Roll(characters, payments, options, random)).Amount)
            .ToArray();

        Assert.All(amounts, amount => Assert.InRange(amount, 120u, 360u));
        Assert.Contains(amounts, amount => amount > 300);
    }

    [Fact]
    public void BothCardKindsAreDrawnWithTheConfiguredRelativeChance()
    {
        uint?[] characters = [1001, null, null, null, null, null, null, null];
        MatchPayment[] payments = [new(1001, 0, MatchEnding.Played, false, 40, 120)];
        var options = SessionHarness.ShippedRewardOptions();
        var random = new Random(11);

        var gold = Enumerable.Range(0, 900)
            .Count(_ => Assert.Single(CardRewards.Roll(characters, payments, options, random)).Kind == CardRewardKind.Gold);

        Assert.InRange(gold, 540, 660);
    }
}
