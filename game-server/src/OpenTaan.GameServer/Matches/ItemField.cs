namespace OpenTaan.GameServer.Matches;

/// <summary>An item box on a match's field: its id, and where it hangs.</summary>
/// <param name="Id">The id it appeared with, which a catch of it names.</param>
/// <param name="X">Where it hangs, across the map.</param>
/// <param name="Y">Where it hangs, down the map: a smaller value is higher up.</param>
public readonly record struct ItemBox(ulong Id, int X, int Y);

/// <summary>
/// The item boxes on a match's field: every box the server or a shooter's client has put
/// there and nobody has caught yet. Changed only under the room directory's lock.
/// </summary>
/// <remarks>
/// <para>
/// A box hangs where it appears until it is caught: the client never moves it, and never
/// takes it away by itself. So the field holds a box from its appearance until a catch of it
/// is seen, by a ghost or by a missile during a shot.
/// </para>
/// <para>
/// The server places its boxes in the strip between the leftmost and the rightmost of the
/// characters in play, each above the character nearest to it, and never within
/// <see cref="Gap"/> of another box, so that no box sits on another.
/// </para>
/// </remarks>
internal sealed class ItemField
{
    /// <summary>How near a character must come to a box to catch it, as the client has it.</summary>
    public const int CatchRadius = 35;

    /// <summary>How near a box the server places another, at the least: two catch radii, so no spot is near enough to both.</summary>
    public const int Gap = 2 * CatchRadius;

    /// <summary>How many places the server draws for a box, each too near another box, before it places one fewer.</summary>
    public const int Tries = 8;

    // Where a client may report a character standing, at most, either way along either
    // axis: further than any map reaches, and near enough that no sum here overflows.
    private const int Reach = 1 << 20;

    // The client makes a box's id from the kind of object a box is, 5, in the top byte, the
    // shooter's slot in the next, and its own count below them. The server's ids lead with a
    // byte the client never writes there, so none is ever one of a client's.
    private const ulong ServerIdMark = 0x10UL << 56;

    private readonly Dictionary<ulong, ItemBox> _boxes = [];
    private ulong _placed;

    /// <summary>How many boxes are on the field.</summary>
    public int Count => _boxes.Count;

    /// <summary>Whether the box with <paramref name="id"/> is on the field.</summary>
    public bool Contains(ulong id) => _boxes.ContainsKey(id);

    /// <summary>Puts a box a shooter's client dropped on the field, and returns whether it is new there.</summary>
    public bool Dropped(ItemBox box) => _boxes.TryAdd(box.Id, box);

    /// <summary>Takes the box with <paramref name="id"/> off the field, and returns whether it was there to catch.</summary>
    public bool Caught(ulong id) => _boxes.Remove(id);

    /// <summary>
    /// Places up to <paramref name="count"/> boxes of the server's own and puts them on the
    /// field: each at a random spot of the strip between the leftmost and the rightmost of
    /// <paramref name="characters"/>, and a random height from <paramref name="lowest"/> to
    /// <paramref name="highest"/> above the one nearest to it. A strip too narrow for the
    /// boxes to stand side by side is widened about its middle until they can. A box is
    /// placed only where it keeps <see cref="Gap"/> from every other box on the field.
    /// </summary>
    /// <param name="characters">Where each character in play stands, in slot order.</param>
    /// <param name="count">How many boxes to place.</param>
    /// <param name="lowest">The least height above a character a box is placed at.</param>
    /// <param name="highest">The greatest height above a character a box is placed at.</param>
    /// <param name="random">Where the spots come from.</param>
    /// <returns>The boxes placed, in order; fewer than <paramref name="count"/> when there was no room.</returns>
    public IReadOnlyList<ItemBox> Place(IReadOnlyList<(int X, int Y)> characters, int count, int lowest, int highest, Random random)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(random);
        if (count <= 0 || characters.Count == 0)
        {
            return [];
        }

        (int X, int Y)[] standing = [.. characters.Select(character => (Math.Clamp(character.X, -Reach, Reach), Math.Clamp(character.Y, -Reach, Reach)))];
        var (left, right) = StripOf(standing, count);
        var placed = new List<ItemBox>();
        for (var box = 0; box < count; box++)
        {
            for (var tryNumber = 0; tryNumber < Tries; tryNumber++)
            {
                var x = left + random.Next(right - left + 1);
                var under = standing.MinBy(character => Math.Abs(character.X - x));
                var y = under.Y - (lowest + random.Next(highest - lowest + 1));
                if (_boxes.Values.All(other => KeepsGap(other, x, y)))
                {
                    var placedBox = new ItemBox(ServerIdMark | ++_placed, x, y);
                    _boxes.Add(placedBox.Id, placedBox);
                    placed.Add(placedBox);
                    break;
                }
            }
        }

        return placed;
    }

    // The strip the boxes go in: from the leftmost character to the rightmost, widened about
    // its middle to fit count boxes side by side, and kept off the left of the map.
    private static (int Left, int Right) StripOf(IReadOnlyList<(int X, int Y)> characters, int count)
    {
        var left = characters.Min(character => character.X);
        var right = characters.Max(character => character.X);
        var needed = (count - 1) * Gap;
        if (right - left < needed)
        {
            left = ((left + right) / 2) - (needed / 2);
            right = left + needed;
        }

        return left < 0 ? (0, right - left) : (left, right);
    }

    private static bool KeepsGap(ItemBox other, int x, int y)
    {
        long dx = other.X - x;
        long dy = other.Y - y;
        return (dx * dx) + (dy * dy) >= (long)Gap * Gap;
    }
}
