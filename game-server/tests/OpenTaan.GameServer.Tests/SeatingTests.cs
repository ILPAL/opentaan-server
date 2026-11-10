using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// Where a connection's characters sit. A room is written as its eight slots in order:
/// <c>.</c> open, <c>x</c> closed, <c>#</c> taken.
/// </summary>
public sealed class SeatingTests
{
    [Fact]
    public void TheLeftTwoColumnsAreTheFirstTeam() =>
        Assert.Equal([1, 1, 2, 2, 1, 1, 2, 2], Enumerable.Range(0, GameSlotInfo.SlotCount).Select(Seating.TeamOf));

    [Theory]
    [InlineData("........", 1, new byte[] { 0 })]
    [InlineData("........", 2, new byte[] { 0, 1 })]
    [InlineData("#.......", 1, new byte[] { 2 })]
    [InlineData("#.......", 2, new byte[] { 2, 3 })]
    [InlineData("#.#.....", 1, new byte[] { 1 })]
    [InlineData("#.#.....", 2, new byte[] { 1, 4 })]
    [InlineData("##......", 2, new byte[] { 2, 3 })]
    [InlineData("##.#....", 1, new byte[] { 2 })]
    [InlineData("##.#....", 2, new byte[] { 2, 6 })]
    public void CharactersSitTogetherInTheLowestFreeSlotsOfTheTeamWithFewer(string room, int characters, byte[] expected)
    {
        Assert.Null(Seating.Choose(Room(room), characters, out var chosen));
        Assert.Equal(expected, chosen);
    }

    [Theory]
    [InlineData("####....", 2, new byte[] { 4, 5 })]
    [InlineData("#.#.#.#.", 2, new byte[] { 1, 5 })]
    public void ATieGoesToTheFirstTeam(string room, int characters, byte[] expected)
    {
        Assert.Null(Seating.Choose(Room(room), characters, out var chosen));
        Assert.Equal(expected, chosen);
    }

    [Theory]
    [InlineData("xx.#.x..", 1, new byte[] { 4 })]
    [InlineData("xx.#.x..", 2, new byte[] { 2, 6 })]
    [InlineData("#.xx#..x", 2, new byte[] { 1, 5 })]
    public void CharactersThatDoNotAllFitTheTeamWithFewerSitTogetherOnTheOther(string room, int characters, byte[] expected)
    {
        Assert.Null(Seating.Choose(Room(room), characters, out var chosen));
        Assert.Equal(expected, chosen);
        Assert.Single(chosen.Select(slot => Seating.TeamOf(slot)).Distinct());
    }

    [Theory]
    [InlineData("#.xx..xx", 1, new byte[] { 1 })]
    [InlineData("#.xx..xx", 2, new byte[] { 1, 4 })]
    [InlineData("##xx#.xx", 1, new byte[] { 5 })]
    public void ClosedSlotsAreNeverChosen(string room, int characters, byte[] expected)
    {
        Assert.Null(Seating.Choose(Room(room), characters, out var chosen));
        Assert.Equal(expected, chosen);
    }

    [Theory]
    [InlineData("########", 1)]
    [InlineData("#######.", 2)]
    [InlineData("##xx##xx", 1)]
    [InlineData("##xx#.xx", 2)]
    [InlineData("x#xx#xxx", 1)]
    public void ARoomWithFewerFreeSlotsThanCharactersIsFull(string room, int characters)
    {
        Assert.Equal(EnterRefusal.RoomFull, Seating.Choose(Room(room), characters, out var chosen));
        Assert.Empty(chosen);
    }

    [Theory]
    [InlineData("####.#.#")]
    [InlineData("#.#####.")]
    [InlineData("#x#.#.x#")]
    [InlineData("#..#####")]
    public void TwoCharactersWithOneFreeSlotOnEachTeamHaveNoSeatTogether(string room)
    {
        Assert.Equal(EnterRefusal.SlotUnavailable, Seating.Choose(Room(room), 2, out var chosen));
        Assert.Empty(chosen);
    }

    private static SlotUse[] Room(string slots) =>
    [
        .. slots.Select(slot => slot switch
        {
            '.' => SlotUse.Open,
            'x' => SlotUse.Closed,
            '#' => SlotUse.Taken,
            _ => throw new ArgumentException($"'{slot}' is not a slot.", nameof(slots)),
        }),
    ];
}
