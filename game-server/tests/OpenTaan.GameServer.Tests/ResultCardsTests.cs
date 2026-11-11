using OpenTaan.GameServer.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.Tests.GameServer;

/// <summary>The result screen's cards: one for each character, each card once.</summary>
public sealed class ResultCardsTests
{
    [Fact]
    public void ACardPickedIsTheCharactersAndNoOneElses()
    {
        var cards = new ResultCards();

        Assert.Equal((CardOutcome.Picked, (byte)3), cards.Pick(0, 3, new Random(1)));
        Assert.True(cards.HasPicked(0));

        Assert.Equal((CardOutcome.Taken, (byte)3), cards.Pick(1, 3, new Random(1)));
        Assert.False(cards.HasPicked(1));

        // A character turns over one card only.
        Assert.Equal((CardOutcome.AlreadyPicked, (byte)3), cards.Pick(0, 4, new Random(1)));
        Assert.Equal((CardOutcome.AlreadyPicked, (byte)3), cards.Pick(0, null, new Random(1)));
    }

    [Theory]
    [InlineData(PrResultSelectCard.CardCount)]
    [InlineData(0xFF)]
    public void ACardPastTheOnesAPlayerMayPickIsNotACard(byte card)
    {
        var cards = new ResultCards();

        Assert.Equal((CardOutcome.NotACard, card), cards.Pick(0, card, new Random(1)));
        Assert.False(cards.HasPicked(0));
    }

    [Fact]
    public void ACardPickedForAPlayerIsOneOfThoseLeft()
    {
        var cards = new ResultCards();
        cards.Pick(0, 0, new Random(1));
        cards.Pick(1, 1, new Random(1));

        // The first card left.
        Assert.Equal((CardOutcome.Picked, (byte)2), cards.Pick(2, null, new ScriptedChance(0)));
    }

    [Fact]
    public void EveryCharacterOfAFullRoomGetsACardOfItsOwn()
    {
        var cards = new ResultCards();
        var random = new Random(7);

        var picked = Enumerable.Range(0, 8).Select(slot => cards.Pick((byte)slot, null, random)).ToArray();

        Assert.All(picked, pick => Assert.Equal(CardOutcome.Picked, pick.Outcome));
        Assert.Equal(8, picked.Select(pick => pick.Card).Distinct().Count());
    }
}
