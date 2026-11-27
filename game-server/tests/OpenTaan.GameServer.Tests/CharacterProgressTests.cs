using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>What a match changes about a character, and the character it makes.</summary>
public sealed class CharacterProgressTests
{
    [Fact]
    public void AMatchAddsItsExperienceAndGoldAndCountsAWinOrALoss()
    {
        var before = new CharacterProgress(1, 20, 500, 3, 4);

        Assert.Equal(new CharacterProgress(1, 80, 680, 4, 4), before.After(60, 180, won: true));
        Assert.Equal(new CharacterProgress(1, 60, 620, 3, 5), before.After(40, 120, won: false));
    }

    [Fact]
    public void ExperiencePastALevelsEndIsAtTheNextLevel()
    {
        Assert.Equal(new CharacterProgress(2, 300, 0, 1, 0), new CharacterProgress(1, 240, 0, 0, 0).After(60, 0, won: true));
        Assert.Equal(new CharacterProgress(3, 1000, 0, 0, 0), new CharacterProgress(1, 0, 0, 0, 0).WithExperience(1000));
    }

    [Fact]
    public void ExperienceStopsAtTheEndOfTheLastLevelsBarAndGoldAndTheRecordAtTheMostTheClientHolds()
    {
        var before = new CharacterProgress(109, 18_063_200, uint.MaxValue - 100, uint.MaxValue, uint.MaxValue);

        Assert.Equal(new CharacterProgress(109, 18_063_236, uint.MaxValue, uint.MaxValue, uint.MaxValue), before.After(60, 180, won: true));
        Assert.Equal(new CharacterProgress(109, 18_063_236, uint.MaxValue, uint.MaxValue, uint.MaxValue), before.After(60, 180, won: false));
    }

    [Fact]
    public void ACharacterWithProgressHasItsLevelsBarAndAnAppearanceCarryingTheLevel()
    {
        var created = new Character(
            1001,
            new CharacterInfo(1001, "Kimi", 1, 0, 0, 300, 0, Gender.Female, 5, 6, 7, 8, 0, 0, 9, 10, 11, string.Empty),
            Character.AppearanceOf(Gender.Female, 1001, 1, new AppearanceSlot[AppearanceSlot.Count]));

        var paid = created.WithProgress(new CharacterProgress(2, 310, 180, 1, 0));

        Assert.Equal(
            new CharacterInfo(1001, "Kimi", 2, 310, 300, 791, 180, Gender.Female, 5, 6, 7, 8, 1, 0, 9, 10, 11, string.Empty),
            paid.Info);
        Assert.Equal(Character.AppearanceOf(Gender.Female, 1001, 2, new AppearanceSlot[AppearanceSlot.Count]), paid.Appearance);
        Assert.Equal(new CharacterProgress(2, 310, 180, 1, 0), CharacterProgress.Of(paid.Info));
    }
}
