using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>What a match pays: the shipped amounts, how each is worked out, and the settings the server refuses to start with.</summary>
public sealed class RewardSettingsTests
{
    [Fact]
    public void TheShippedRewardsAreTheAmountsAgreedAndTheHarnessStandsInForThem()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build()
            .GetSection(RewardOptions.Section)
            .Get<RewardOptions>()!;

        Assert.Equal((40u, 120u, 1.5), (shipped.TeamMatchExperience, shipped.TeamMatchGold, shipped.WinMultiplier));
        Assert.Equal([(ushort)0], shipped.MissionExperience.Keys);
        Assert.Equal([40u, 50u, 60u], shipped.MissionExperience[0]);
        Assert.Equal((0.25, 1.5), (shipped.MissionLostShare, shipped.MissionGoldPerExperience));
        Assert.Equal((60, 30, 40u, 120u, 50u, 20u),
            (shipped.CardGoldWeight, shipped.CardExperienceWeight, shipped.CardMinimumPercent,
                shipped.CardMaximumPercent, shipped.CardGoldFloor, shipped.CardExperienceFloor));
        Assert.Equal((3, TimeSpan.FromSeconds(2)), (shipped.PayoutAttempts, shipped.PayoutRetryDelay));
        Assert.Null(shipped.Problem(SessionHarness.ShippedRoomOptions().Missions.Keys));

        var harness = SessionHarness.ShippedRewardOptions();
        Assert.Equal(
            (shipped.TeamMatchExperience, shipped.TeamMatchGold, shipped.WinMultiplier, shipped.MissionLostShare, shipped.MissionGoldPerExperience),
            (harness.TeamMatchExperience, harness.TeamMatchGold, harness.WinMultiplier, harness.MissionLostShare, harness.MissionGoldPerExperience));
        Assert.Equal(shipped.MissionExperience[0], harness.MissionExperience[0]);
        Assert.Equal(
            (shipped.CardGoldWeight, shipped.CardExperienceWeight, shipped.CardMinimumPercent,
                shipped.CardMaximumPercent, shipped.CardGoldFloor, shipped.CardExperienceFloor),
            (harness.CardGoldWeight, harness.CardExperienceWeight, harness.CardMinimumPercent,
                harness.CardMaximumPercent, harness.CardGoldFloor, harness.CardExperienceFloor));
        Assert.Equal((shipped.PayoutAttempts, shipped.PayoutRetryDelay), (harness.PayoutAttempts, harness.PayoutRetryDelay));
    }

    [Fact]
    public void AMatchBetweenTeamsPaysTheLosers40And120AndTheWinnersHalfAsMuchAgain()
    {
        var rewards = SessionHarness.ShippedRewardOptions();

        Assert.Equal(new Earning(60, 180), rewards.BetweenTeams(won: true));
        Assert.Equal(new Earning(40, 120), rewards.BetweenTeams(won: false));
    }

    [Theory]
    [InlineData((byte)0, true, 40u, 60u)]
    [InlineData((byte)1, true, 50u, 75u)]
    [InlineData((byte)2, true, 60u, 90u)]
    [InlineData((byte)0, false, 10u, 15u)]
    [InlineData((byte)1, false, 12u, 18u)]
    [InlineData((byte)2, false, 15u, 22u)]
    public void AMissionPaysByItsDifficultyAQuarterWhenLostAndOneAndAHalfGoldForEachExperienceFractionsDropped(
        byte difficulty, bool cleared, uint experience, uint gold) =>
        Assert.Equal(new Earning(experience, gold), SessionHarness.ShippedRewardOptions().Mission(0, difficulty, cleared));

    [Fact]
    public void NoMatchPaysMoreExperienceThanTheFirstLevelTakesButItsGoldIsPaidInFull()
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        rewards.TeamMatchExperience = 1000;
        rewards.TeamMatchGold = 3000;
        rewards.MissionExperience = new Dictionary<ushort, IReadOnlyList<uint>> { [0] = [400, 0, 0] };

        Assert.Equal(new Earning(300, 4500), rewards.BetweenTeams(won: true));
        Assert.Equal(new Earning(300, 600), rewards.Mission(0, 0, cleared: true));
    }

    [Fact]
    public void AFractionIsDroppedEvenAtAHalfNeverRoundedUpOrToEven()
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        rewards.TeamMatchExperience = 7;
        rewards.TeamMatchGold = 25;
        rewards.MissionExperience = new Dictionary<ushort, IReadOnlyList<uint>> { [0] = [13, 0, 0] };

        Assert.Equal(new Earning(10, 37), rewards.BetweenTeams(won: true));
        Assert.Equal(new Earning(3, 4), rewards.Mission(0, 0, cleared: false));
    }

    [Fact]
    public void AnAmountTooLargeForTheClientIsTheMostItHolds()
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        rewards.TeamMatchGold = uint.MaxValue;

        Assert.Equal(uint.MaxValue, rewards.BetweenTeams(won: true).Gold);
    }

    [Theory]
    [InlineData("a mission the rooms list without its experience", "gives no experience for mission 1")]
    [InlineData("experience for a mission the rooms do not list", "gives experience for mission 5")]
    [InlineData("two amounts for three difficulties", "one amount for each of the 3 difficulties")]
    [InlineData("a negative win multiplier", "WinMultiplier must be a number from 0 up")]
    [InlineData("a lost mission paying more than a clear", "MissionLostShare must be from 0 to 1")]
    [InlineData("negative gold per experience", "MissionGoldPerExperience must be a number from 0 up")]
    [InlineData("no try at a payment", "PayoutAttempts must be at least 1")]
    [InlineData("a negative wait between tries", "PayoutRetryDelay cannot be negative")]
    [InlineData("no card chance", "CardGoldWeight and CardExperienceWeight must be nonnegative")]
    [InlineData("negative card chance", "CardGoldWeight and CardExperienceWeight must be nonnegative")]
    [InlineData("reversed card range", "CardMinimumPercent must be no greater than CardMaximumPercent")]
    [InlineData("zero card floor", "CardGoldFloor and CardExperienceFloor must both be positive")]
    public void RewardsTheServerCannotPayWithAreRefusedNamingWhatIsWrong(string settings, string named)
    {
        var rewards = SessionHarness.ShippedRewardOptions();
        IReadOnlyList<ushort> missions = [0];
        switch (settings)
        {
            case "a mission the rooms list without its experience":
                missions = [0, 1];
                break;
            case "experience for a mission the rooms do not list":
                rewards.MissionExperience = new Dictionary<ushort, IReadOnlyList<uint>> { [0] = [40, 50, 60], [5] = [1, 2, 3] };
                break;
            case "two amounts for three difficulties":
                rewards.MissionExperience = new Dictionary<ushort, IReadOnlyList<uint>> { [0] = [40, 50] };
                break;
            case "a negative win multiplier":
                rewards.WinMultiplier = -0.5;
                break;
            case "a lost mission paying more than a clear":
                rewards.MissionLostShare = 1.25;
                break;
            case "negative gold per experience":
                rewards.MissionGoldPerExperience = double.NegativeInfinity;
                break;
            case "no try at a payment":
                rewards.PayoutAttempts = 0;
                break;
            case "a negative wait between tries":
                rewards.PayoutRetryDelay = TimeSpan.FromSeconds(-1);
                break;
            case "no card chance":
                rewards.CardGoldWeight = 0;
                rewards.CardExperienceWeight = 0;
                break;
            case "negative card chance":
                rewards.CardGoldWeight = -1;
                break;
            case "reversed card range":
                rewards.CardMinimumPercent = 121;
                break;
            case "zero card floor":
                rewards.CardGoldFloor = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var rooms = SessionHarness.ShippedRoomOptions();
        rooms.Missions = missions.ToDictionary(mission => mission, _ => (byte)4);
        var result = new RewardOptionsValidation(Options.Create(rooms)).Validate(null, rewards);

        Assert.True(result.Failed);
        Assert.Contains(named, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedRewardsAreAcceptedForTheShippedRooms() =>
        Assert.True(new RewardOptionsValidation(Options.Create(SessionHarness.ShippedRoomOptions()))
            .Validate(null, SessionHarness.ShippedRewardOptions()).Succeeded);
}
