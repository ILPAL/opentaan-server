using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

public sealed class MvpBonusTests
{
    [Theory]
    [InlineData(301u, (byte)0, 0u, 300u, (byte)3, 2u)]
    [InlineData(300u, (byte)2, 0u, 300u, (byte)1, 2u)]
    [InlineData(300u, (byte)1, 2u, 300u, (byte)1, 1u)]
    public void TheLeaderIsChosenByDamageThenKillsThenAssists(
        uint leaderDamage, byte leaderKills, uint leaderAssists, uint otherDamage, byte otherKills, uint otherAssists)
    {
        var other = new MatchPayment(21, 0, MatchEnding.Played, Won: true, 60, 180);
        var leader = new MatchPayment(22, 7, MatchEnding.Played, Won: false, 40, 120);
        MatchPayment[] payments = [other, leader];
        var stats = new MatchStatistics[GameSlotInfo.SlotCount];
        stats[other.Slot] = new MatchStatistics(otherKills, otherDamage, 500, 1f) { Assists = otherAssists };
        stats[leader.Slot] = new MatchStatistics(leaderKills, leaderDamage, 200, 0.5f) { Assists = leaderAssists };

        MatchRules.AwardMvp(payments, stats);

        Assert.Equal(other, payments[0]);
        Assert.Equal(leader with { Experience = 140, Gold = 220, MvpBonus = new ResultBonus(4, 100, 100) }, payments[1]);
    }

    [Fact]
    public void AnExactTieForFirstAwardsNeitherPlayer()
    {
        MatchPayment[] payments =
        [
            new(21, 0, MatchEnding.Played, Won: true, 60, 180),
            new(22, 1, MatchEnding.Played, Won: true, 60, 180),
            new(23, 2, MatchEnding.Played, Won: false, 40, 120),
        ];
        var stats = new MatchStatistics[GameSlotInfo.SlotCount];
        stats[0] = new MatchStatistics(1, 300, 100, 1f) { Assists = 1 };
        stats[1] = new MatchStatistics(1, 299, 100, 1f) { Assists = 1 };
        stats[2] = new MatchStatistics(1, 300, 200, 0.5f) { Assists = 1 };

        MatchRules.AwardMvp(payments, stats);

        Assert.All(payments, payment => Assert.Null(payment.MvpBonus));
    }

    [Fact]
    public void PlayersWhoLeftOrGaveUpAreIneligibleEvenWithHigherStats()
    {
        var left = new MatchPayment(21, 0, MatchEnding.Left, Won: false, 0, 0);
        var gaveUp = new MatchPayment(22, 1, MatchEnding.GaveUp, Won: true, 0, 0);
        var finished = new MatchPayment(23, 2, MatchEnding.Played, Won: false, 40, 120);
        MatchPayment[] payments = [left, gaveUp, finished];
        var stats = new MatchStatistics[GameSlotInfo.SlotCount];
        stats[0] = new MatchStatistics(3, 3000, 0, 1f);
        stats[1] = new MatchStatistics(2, 2000, 0, 1f);
        stats[2] = new MatchStatistics(0, 100, 0, 1f);

        MatchRules.AwardMvp(payments, stats);

        Assert.Equal(left, payments[0]);
        Assert.Equal(gaveUp, payments[1]);
        Assert.Equal(new ResultBonus(ResultBonus.MedalType, 100, 100), payments[2].MvpBonus);
    }

    [Fact]
    public void ItemGiftsAloneDoNotQualifyEvenWhenOnlyOnePlayerFinishes()
    {
        var player = new MatchPayment(21, 0, MatchEnding.Played, Won: true, 60, 180);
        MatchPayment[] payments = [player];
        var stats = new MatchStatistics[GameSlotInfo.SlotCount];
        stats[0] = new MatchStatistics(0, 0, 50, 0f);

        MatchRules.AwardMvp(payments, stats);

        Assert.Equal(player, payments[0]);
    }

    [Theory]
    [InlineData(uint.MaxValue - 31, 31u)]
    [InlineData(uint.MaxValue, 0u)]
    public void TheBonusGoldStopsAtThePaymentLimit(uint baseGold, uint bonusGold)
    {
        var player = new MatchPayment(21, 0, MatchEnding.Played, Won: true, 300, baseGold);
        MatchPayment[] payments = [player];
        var stats = new MatchStatistics[GameSlotInfo.SlotCount];
        stats[0] = new MatchStatistics(1, 300, 0, 1f);

        MatchRules.AwardMvp(payments, stats);

        Assert.Equal(player with { Experience = 400, Gold = uint.MaxValue, MvpBonus = new ResultBonus(ResultBonus.MedalType, 100, bonusGold) },
            payments[0]);
    }
}
