using OpenTaan.GameServer.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Where the server places the item boxes of a match between teams, and what the field
/// holds: the strip between the characters, the height above the nearest one, the gap kept
/// from every other box, and the widening of a strip too narrow. Every value is invented.
/// </summary>
/// <remarks>
/// The chance is scripted: each box takes two draws, its place along the strip and then its
/// height, so a test lines up both and checks how many things each draw was among.
/// </remarks>
public sealed class ItemFieldTests
{
    private const int Lowest = 100;
    private const int Highest = 300;
    private const int Heights = Highest - Lowest + 1;

    [Fact]
    public void BoxesArePlacedAlongTheStripFromTheLeftmostCharacterToTheRightmost()
    {
        var chance = Chance(0, 0, 800, 200);
        var field = new ItemField();

        var placed = field.Place([(100, 900), (900, 800)], count: 2, Lowest, Highest, chance);

        // The first at the strip's left end, 100 up; the second at its right end, 300 up.
        Assert.Equal<(int, int)[]>([(100, 800), (900, 500)], [.. placed.Select(box => (box.X, box.Y))]);
        Assert.Equal([801, Heights, 801, Heights], chance.DrawnAmong);
        Assert.Equal(2, field.Count);
    }

    [Fact]
    public void ABoxsHeightIsMeasuredFromTheCharacterNearestToIt()
    {
        var chance = Chance(301, 0, 200, 0);
        var field = new ItemField();

        var placed = field.Place([(100, 900), (500, 600)], count: 2, Lowest, Highest, chance);

        // At 401 the character at 500 is nearest; at 300 both are as near, and the first one, in slot order, counts.
        Assert.Equal<(int, int)[]>([(401, 500), (300, 800)], [.. placed.Select(box => (box.X, box.Y))]);
    }

    [Fact]
    public void NoBoxIsPlacedNearerThanTheGapToAnotherOnTheFieldTheShootersToo()
    {
        var field = new ItemField();
        Assert.True(field.Dropped(new ItemBox(0x0503_0000_0000_0001, 300, 700)));

        // On the shooter's box, then one short of the gap from it, then the gap.
        var chance = Chance(200, 100, 200 + ItemField.Gap - 1, 100, 200 + ItemField.Gap, 100);
        var placed = field.Place([(100, 900), (500, 900)], count: 1, Lowest, Highest, chance);

        Assert.Equal((300 + ItemField.Gap, 700), (Assert.Single(placed).X, placed[0].Y));
        Assert.Equal(6, chance.DrawnAmong.Count);
        Assert.Equal(2, field.Count);
    }

    [Fact]
    public void AStripTooNarrowForTheBoxesSideBySideIsWidenedAboutItsMiddleJustEnough()
    {
        var chance = Chance(0, 0, ItemField.Gap, 0, 2 * ItemField.Gap, 0);
        var field = new ItemField();

        // One character alone, as in a match whose other players stand together.
        var placed = field.Place([(500, 900)], count: 3, Lowest, Highest, chance);

        Assert.Equal<(int, int)[]>([(430, 800), (500, 800), (570, 800)], [.. placed.Select(box => (box.X, box.Y))]);
        Assert.Equal((2 * ItemField.Gap) + 1, chance.DrawnAmong[0]);
    }

    [Fact]
    public void AStripWideEnoughForTheBoxesIsLeftAsTheCharactersMakeIt()
    {
        var chance = Chance(0, 0);
        var field = new ItemField();

        var placed = field.Place([(300, 900), (300 + (2 * ItemField.Gap), 900)], count: 3, Lowest, Highest, chance);

        Assert.Equal(300, placed[0].X);
        Assert.Equal((2 * ItemField.Gap) + 1, chance.DrawnAmong[0]);
    }

    [Fact]
    public void AStripWidenedPastTheLeftOfTheMapIsMovedBackOntoIt()
    {
        var chance = Chance(0, 0);
        var field = new ItemField();

        var placed = field.Place([(10, 900)], count: 3, Lowest, Highest, chance);

        Assert.Equal(0, placed[0].X);
        Assert.Equal((2 * ItemField.Gap) + 1, chance.DrawnAmong[0]);
    }

    [Fact]
    public void FewerBoxesArePlacedWhenEveryPlaceDrawnForOneIsTooNearAnother()
    {
        // Every draw lands on the first box placed.
        var chance = new ScriptedChance(seed: 0);
        var field = new ItemField();

        var placed = field.Place([(500, 900)], count: 3, Lowest, Highest, chance);

        Assert.Single(placed);
        Assert.Equal(2 + (2 * 2 * ItemField.Tries), chance.DrawnAmong.Count);
    }

    [Fact]
    public void TheServersBoxesHaveIdsOfTheirOwnThatNoClientsBoxEverHas()
    {
        var field = new ItemField();
        var clients = new ItemBox(0x0503_0000_0000_0001, 0, 0);
        Assert.True(field.Dropped(clients));

        var placed = field.Place([(0, 900), (5000, 900)], count: 3, Lowest, Highest, new Random(1));

        Assert.Equal(3, placed.Count);
        Assert.Equal(3, placed.Select(box => box.Id).Distinct().Count());
        Assert.All(placed, box => Assert.NotEqual(clients.Id >> 56, box.Id >> 56));
        Assert.All(placed, box => Assert.True(field.Contains(box.Id)));
    }

    [Fact]
    public void ABoxIsOnTheFieldFromItsAppearanceUntilItIsCaughtOnce()
    {
        var field = new ItemField();
        var box = new ItemBox(0x0501_0000_0000_0002, 10, 20);

        Assert.True(field.Dropped(box));
        Assert.False(field.Dropped(box));
        Assert.True(field.Caught(box.Id));
        Assert.False(field.Caught(box.Id));
        Assert.False(field.Contains(box.Id));
        Assert.Equal(0, field.Count);
    }

    [Fact]
    public void NothingIsPlacedOrDrawnForNoBoxesOrWhenNoCharacterIsInPlay()
    {
        var chance = new ScriptedChance(seed: 0);
        var field = new ItemField();

        Assert.Empty(field.Place([(500, 900)], count: 0, Lowest, Highest, chance));
        Assert.Empty(field.Place([], count: 3, Lowest, Highest, chance));
        Assert.Empty(chance.DrawnAmong);
    }

    [Fact]
    public void CharactersReportedFurtherThanAnyMapReachesStillGetTheirBoxes()
    {
        // Every draw at the strip's left end, as far from the leftmost character as any spot can be.
        var field = new ItemField();

        var placed = field.Place([(int.MinValue, int.MinValue), (int.MaxValue, int.MaxValue)], count: 3, Lowest, Highest, new ScriptedChance(seed: 0));

        Assert.Single(placed);
    }

    // Chance whose next draws take the things at these places, in order.
    private static ScriptedChance Chance(params int[] draws)
    {
        var chance = new ScriptedChance(seed: 0);
        foreach (var draw in draws)
        {
            chance.Draws.Enqueue(draw);
        }

        return chance;
    }
}
