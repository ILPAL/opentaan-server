using OpenTaan.GameServer.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>The table of levels a character's level and experience bar are worked out from.</summary>
public sealed class LevelTableTests
{
    [Fact]
    public void TheFirstLevelsRunFromNothingThroughTheCostsAgreedForThem()
    {
        Assert.Equal(new ExperienceBar(0, 300), LevelTable.BarAt(1));
        Assert.Equal(new ExperienceBar(300, 791), LevelTable.BarAt(2));
        Assert.Equal(new ExperienceBar(791, 1449), LevelTable.BarAt(3));
        Assert.Equal(new ExperienceBar(1449, 2310), LevelTable.BarAt(4));
        Assert.Equal(new ExperienceBar(2310, 3373), LevelTable.BarAt(5));
        Assert.Equal(300u, LevelTable.FirstLevelCost);
    }

    [Fact]
    public void TheLastLevelIs109ReachedAt17519999AndItsBarEndsWhereExperienceStops()
    {
        Assert.Equal(109, LevelTable.LastLevel);
        Assert.Equal(new ExperienceBar(16_991_317, 17_519_999), LevelTable.BarAt(108));
        Assert.Equal(new ExperienceBar(17_519_999, 18_063_236), LevelTable.BarAt(109));
        Assert.Equal(18_063_236u, LevelTable.MostExperience);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)110)]
    [InlineData(ushort.MaxValue)]
    public void ThereIsNoLevelBelowTheFirstOrPastTheLast(ushort level) => Assert.Null(LevelTable.BarAt(level));

    [Fact]
    public void EachLevelBeginsWhereTheOneBelowEndsAndCostsMoreThanItSoNoneCostsLessThanTheFirst()
    {
        var bars = Enumerable.Range(1, LevelTable.LastLevel).Select(level => LevelTable.BarAt((ushort)level)!.Value).ToArray();

        Assert.All(bars.Skip(1).Zip(bars), pair => Assert.Equal(pair.Second.Ceiling, pair.First.Floor));
        Assert.All(bars.Skip(1).Zip(bars), pair =>
            Assert.True(pair.First.Ceiling - pair.First.Floor > pair.Second.Ceiling - pair.Second.Floor, $"level at {pair.First.Floor}"));
        Assert.All(bars, bar => Assert.True(bar.Ceiling - bar.Floor >= LevelTable.FirstLevelCost));
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(299u, 1)]
    [InlineData(300u, 2)]
    [InlineData(790u, 2)]
    [InlineData(791u, 3)]
    [InlineData(17_519_998u, 108)]
    [InlineData(17_519_999u, 109)]
    [InlineData(18_063_236u, 109)]
    [InlineData(uint.MaxValue, 109)]
    public void TheLevelIsTheLastWhoseBeginningTheExperienceHasReached(uint experience, int level) =>
        Assert.Equal(level, LevelTable.LevelOf(experience));
}
