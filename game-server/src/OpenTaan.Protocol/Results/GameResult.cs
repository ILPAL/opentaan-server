using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Results;

/// <summary>How a match ended: which team won, and a row for each of the room's slots.</summary>
/// <remarks>
/// <para>
/// The client reads exactly <see cref="GameSlotInfo.SlotCount"/> rows, by position and
/// without checking how many there are, so every result carries a row for each slot, in
/// order: one with fewer cannot be built or read.
/// </para>
/// <para>
/// The client has no draw: it shows a winning team of 0 as team 1's win. So the winner is
/// always a team, 1 or 2. The client adds a win or a loss to each of its own characters by
/// comparing their team with it.
/// </para>
/// </remarks>
/// <param name="WinningTeam">The team that won, 1 or 2.</param>
/// <param name="Slots">A row for each of the room's slots, in order.</param>
/// <param name="WinnerPointDelta">The arena points the winners gain, shown only in arena channels.</param>
/// <param name="LoserPointDelta">The arena points the losers lose, shown only in arena channels.</param>
[WireClass("GameResult")]
public sealed record GameResult(byte WinningTeam, IReadOnlyList<GameResultSlot> Slots, int WinnerPointDelta, int LoserPointDelta)
{
    /// <summary>The fewest bytes a result occupies: its id, the team, an empty list and the two deltas.</summary>
    internal const int MinimumSize = sizeof(uint) + sizeof(byte) + sizeof(uint) + (2 * sizeof(int));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameResult>.RegistryId;

    /// <summary>The team that won, 1 or 2.</summary>
    public byte WinningTeam { get; } = WinningTeam is 1 or 2
        ? WinningTeam
        : throw new ArgumentOutOfRangeException(nameof(WinningTeam), "A match is won by team 1 or team 2.");

    /// <summary>A row for each of the room's slots, in order.</summary>
    public IReadOnlyList<GameResultSlot> Slots { get; } = RowsEverySlotInOrder(Slots ?? throw new ArgumentNullException(nameof(Slots)))
        ? Slots
        : throw new ArgumentException($"A result has a row for each of the {GameSlotInfo.SlotCount} slots, in order.", nameof(Slots));

    /// <summary>Whether both results say the same: every field equal, and every row equal in order.</summary>
    public bool Equals(GameResult? other) =>
        other is not null
        && WinningTeam == other.WinningTeam
        && Slots.SequenceEqual(other.Slots)
        && WinnerPointDelta == other.WinnerPointDelta
        && LoserPointDelta == other.LoserPointDelta;

    /// <summary>A hash of the fields and every row, consistent with <see cref="Equals(GameResult)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WinningTeam);
        foreach (var slot in Slots)
        {
            hash.Add(slot);
        }

        hash.Add(WinnerPointDelta);
        hash.Add(LoserPointDelta);
        return hash.ToHashCode();
    }

    /// <summary>Reads the result, or returns null once the read has failed.</summary>
    internal static GameResult? ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var winningTeam = reader.ReadByte();
        var slots = reader.ReadList(GameResultSlot.MinimumSize, GameResultSlot.ReadFrom);
        var winnerPointDelta = reader.ReadInt32();
        var loserPointDelta = reader.ReadInt32();
        if (winningTeam is not (1 or 2) || !RowsEverySlotInOrder(slots))
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        return reader.Error == PacketError.None ? new GameResult(winningTeam, slots, winnerPointDelta, loserPointDelta) : null;
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(WinningTeam);
        writer.WriteList(Slots, static (list, slot) => slot.WriteTo(list));
        writer.WriteInt32(WinnerPointDelta);
        writer.WriteInt32(LoserPointDelta);
    }

    private static bool RowsEverySlotInOrder(IReadOnlyList<GameResultSlot> slots) =>
        slots.Count == GameSlotInfo.SlotCount && slots.Select((slot, index) => slot.SlotIndex == index).All(inPlace => inPlace);
}
