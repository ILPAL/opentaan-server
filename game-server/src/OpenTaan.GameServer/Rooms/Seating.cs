using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>What a room's slot holds, as far as seating anyone in it goes.</summary>
public enum SlotUse
{
    /// <summary>Empty, and anyone entering may take it.</summary>
    Open,

    /// <summary>Empty, and closed to anyone entering.</summary>
    Closed,

    /// <summary>Holding a character.</summary>
    Taken,
}

/// <summary>Which team each slot is on, and where a connection's characters sit.</summary>
/// <remarks>
/// <para>
/// The client draws a room's slots four to a row, the left two columns one team and the
/// right two the other, and takes a slot's team from where it is drawn: slots 0, 1, 4
/// and 5 are team 1, and 2, 3, 6 and 7 team 2.
/// </para>
/// <para>
/// A connection's characters always sit on one team. The client takes a connection's
/// team from the first slot it holds and moves all of them together when it changes
/// team, so a connection split across the two would be shown, and moved, as one team.
/// </para>
/// </remarks>
public static class Seating
{
    /// <summary>The team slot <paramref name="slotIndex"/> is on: 1 or 2.</summary>
    public static byte TeamOf(int slotIndex) => (slotIndex & 3) < 2 ? (byte)1 : (byte)2;

    /// <summary>
    /// Chooses the slots <paramref name="characters"/> characters take in a room whose
    /// slots hold <paramref name="slots"/>, or says why there are none.
    /// </summary>
    /// <remarks>
    /// The characters go to the team with fewer characters when they all fit there, the
    /// first team when both have as many, and otherwise to the other team, each in the
    /// lowest free slots. A room with too few free slots is full; one with enough, but not
    /// on one team, has no slot for them together.
    /// </remarks>
    /// <param name="slots">What each of the room's slots holds, by index.</param>
    /// <param name="characters">How many characters are to sit together.</param>
    /// <param name="chosen">The slots chosen, in rising order, or empty when there are none.</param>
    /// <returns>Null when slots were chosen; otherwise why the characters cannot sit.</returns>
    public static EnterRefusal? Choose(IReadOnlyList<SlotUse> slots, int characters, out IReadOnlyList<byte> chosen)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentOutOfRangeException.ThrowIfLessThan(characters, 1);

        chosen = [];
        var free = new List<byte>[] { [], [] };
        var seated = new int[2];
        for (var slot = 0; slot < slots.Count; slot++)
        {
            var team = TeamOf(slot) - 1;
            if (slots[slot] == SlotUse.Open)
            {
                free[team].Add((byte)slot);
            }
            else if (slots[slot] == SlotUse.Taken)
            {
                seated[team]++;
            }
        }

        if (free[0].Count + free[1].Count < characters)
        {
            return EnterRefusal.RoomFull;
        }

        var first = seated[0] <= seated[1] ? 0 : 1;
        foreach (var team in new[] { first, 1 - first })
        {
            if (free[team].Count >= characters)
            {
                chosen = free[team].GetRange(0, characters);
                return null;
            }
        }

        return EnterRefusal.SlotUnavailable;
    }
}
