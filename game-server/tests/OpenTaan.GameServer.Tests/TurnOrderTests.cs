using OpenTaan.GameServer.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Whose turn comes next: the teams take turns, each going through its own characters in
/// slot order. Slots 0, 1, 4 and 5 are the first team, and 2, 3, 6 and 7 the second.
/// </summary>
public sealed class TurnOrderTests
{
    [Fact]
    public void TwoAgainstTwoTakeTurnsTeamByTeamEachInSlotOrder() =>
        Assert.Equal([2, 1, 3, 0, 2, 1], Turns(first: 0, inPlay: [0, 1, 2, 3], count: 6));

    [Fact]
    public void FourAgainstFourGoThroughEveryCharacterBeforeAnyPlaysAgain() =>
        Assert.Equal([2, 1, 3, 4, 6, 5, 7, 0, 2], Turns(first: 0, inPlay: [0, 1, 2, 3, 4, 5, 6, 7], count: 9));

    [Fact]
    public void ATeamOfOneTakesEveryOtherTurn() =>
        Assert.Equal([2, 1, 2, 0, 2, 1], Turns(first: 0, inPlay: [0, 1, 2], count: 6));

    [Fact]
    public void TheSecondTeamMayStart() =>
        Assert.Equal([0, 3, 1, 2, 0], Turns(first: 2, inPlay: [0, 1, 2, 3], count: 5));

    [Fact]
    public void PlayersAllOnOneTeamGoInSlotOrderAndRoundAgain() =>
        Assert.Equal([1, 4, 0, 1], Turns(first: 0, inPlay: [0, 1, 4], count: 4));

    [Fact]
    public void ATeamWithNoCharacterInPlayIsPassedOver() =>
        Assert.Equal([1, 0, 1], Turns(first: 0, inPlay: [0, 1], count: 3));

    [Fact]
    public void ACharacterOutOfPlayIsPassedOver() =>
        Assert.Equal([2, 0, 3, 0, 2], Turns(first: 0, inPlay: [0, 2, 3], count: 5));

    [Fact]
    public void ATeamGoesOnFromItsLastToPlayEvenOnceThatCharacterIsOut()
    {
        var order = new TurnOrder();
        foreach (var slot in new byte[] { 0, 2, 1, 3, 4, 6 })
        {
            order.Played(slot);
        }

        // The first team's last to play, in slot 4, is out: its next is still the one after it.
        Assert.Equal((byte)5, order.After(6, [0, 1, 5, 2, 3, 6, 7]));
    }

    [Fact]
    public void TheOnlyCharacterInPlayPlaysAgain()
    {
        var order = new TurnOrder();
        order.Played(3);

        Assert.Equal((byte)3, order.After(3, [3]));
    }

    [Fact]
    public void NoTurnFollowsWhenNoCharacterIsInPlay() => Assert.Null(new TurnOrder().After(0, []));

    // The turns after the first, each going to the character the order names.
    private static byte[] Turns(byte first, byte[] inPlay, int count)
    {
        var order = new TurnOrder();
        order.Played(first);
        var turns = new List<byte>();
        var slot = first;
        for (var turn = 0; turn < count; turn++)
        {
            slot = order.After(slot, inPlay)!.Value;
            order.Played(slot);
            turns.Add(slot);
        }

        return [.. turns];
    }
}
