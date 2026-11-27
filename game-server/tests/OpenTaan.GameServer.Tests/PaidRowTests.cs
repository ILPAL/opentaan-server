using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.Tests.GameServer;

/// <summary>A paid character's row in a match's result: its experience bar from where it stood to where the pay takes it.</summary>
public sealed class PaidRowTests
{
    [Fact]
    public void APayThatStaysOnTheLevelIsTwoStepsOnItsBarWithThePayShownRising() =>
        Assert.Equal(
            new GameResultSlot(3, 1, 60, [new ExpResult(1, 20, 0, 300), new ExpResult(1, 80, 0, 300)], 180, 0, 0, 0, 0, [], 0, 0, 0, true, 60, 180),
            MatchRules.PaidRow(3, new CharacterProgress(1, 20, 500, 2, 2), new Earning(60, 180)));

    [Fact]
    public void APayPastTheLevelsEndEndsOnTheNextLevelsBarAtTheNewLevel() =>
        Assert.Equal(
            new GameResultSlot(0, 2, 60, [new ExpResult(1, 250, 0, 300), new ExpResult(2, 310, 300, 791)], 180, 0, 0, 0, 0, [], 0, 0, 0, true, 60, 180),
            MatchRules.PaidRow(0, new CharacterProgress(1, 250, 0, 0, 0), new Earning(60, 180)));

    [Fact]
    public void EachLevelPassedOnTheWayIsAStepAtItsBeginning() =>
        Assert.Equal(
            [new ExpResult(1, 0, 0, 300), new ExpResult(2, 300, 300, 791), new ExpResult(3, 1000, 791, 1449)],
            MatchRules.PaidRow(0, new CharacterProgress(1, 0, 0, 0, 0), new Earning(1000, 0)).Levels);

    [Fact]
    public void APayAtTheLastLevelsEndStaysThereShowingWhatWasPaid() =>
        Assert.Equal(
            new GameResultSlot(1, 109, 60, [new ExpResult(109, 18_063_236, 17_519_999, 18_063_236), new ExpResult(109, 18_063_236, 17_519_999, 18_063_236)], 180, 0, 0, 0, 0, [], 0, 0, 0, true, 60, 180),
            MatchRules.PaidRow(1, new CharacterProgress(109, 18_063_236, 0, 0, 0), new Earning(60, 180)));
}
