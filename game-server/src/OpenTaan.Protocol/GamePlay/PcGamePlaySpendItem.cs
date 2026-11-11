using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>The items a shot spent: the shooter's client sends it as it fires, just before the shot.</summary>
/// <remarks>
/// Every client clears the lists of active items it names: the character's own, its team's
/// and, for a ghost, its ghost team's, each skipped when it carries 0xFF. The client looks
/// those up by slot and by team without checking either, so each is in range or 0xFF. The
/// client acts on the server's copy, so it goes to everyone in the match, the sender
/// included.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that fired, below <see cref="GameSlotInfo.SlotCount"/>, or 0xFF for none.</param>
/// <param name="Team">The character's team, or 0xFF for none.</param>
/// <param name="GhostTeam">The character's team when it is a ghost, or 0xFF.</param>
/// <param name="Items">The items spent.</param>
[WireClass("PcGamePlay_SpendItem")]
public sealed record PcGamePlaySpendItem(byte SlotIndex, byte Team, byte GhostTeam, IReadOnlyList<SpentItem> Items) : IPacket
{
    /// <summary>The value the slot carries for no character.</summary>
    public const byte NoSlot = 0xFF;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlaySpendItem>.RegistryId;

    /// <summary>The slot of the character that fired, below <see cref="GameSlotInfo.SlotCount"/>, or <see cref="NoSlot"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount || SlotIndex == NoSlot
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}, and {NoSlot} names none.");

    /// <summary>The character's team, or 0xFF for none.</summary>
    public byte Team { get; } = TeamNumber.IsTeamOrNone(Team)
        ? Team
        : throw new ArgumentOutOfRangeException(nameof(Team), $"Teams are numbered 0 to {PcGamePlayTurnStart.TeamCount - 1}, and {TeamNumber.None} names none.");

    /// <summary>The character's team when it is a ghost, or 0xFF.</summary>
    public byte GhostTeam { get; } = TeamNumber.IsTeamOrNone(GhostTeam)
        ? GhostTeam
        : throw new ArgumentOutOfRangeException(nameof(GhostTeam), $"Teams are numbered 0 to {PcGamePlayTurnStart.TeamCount - 1}, and {TeamNumber.None} names none.");

    /// <summary>The items spent.</summary>
    public IReadOnlyList<SpentItem> Items { get; } = Items ?? throw new ArgumentNullException(nameof(Items));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlaySpendItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var team = reader.ReadByte();
        var ghostTeam = reader.ReadByte();
        var items = reader.ReadList(SpentItem.Size, static (ref PacketReader list) => new SpentItem(list.ReadUInt16(), list.ReadByte()));
        if ((slotIndex >= GameSlotInfo.SlotCount && slotIndex != NoSlot) || !TeamNumber.IsTeamOrNone(team) || !TeamNumber.IsTeamOrNone(ghostTeam))
        {
            reader.Fail(PacketError.UnexpectedValue);
            (slotIndex, team, ghostTeam) = (NoSlot, TeamNumber.None, TeamNumber.None);
        }

        return reader.Finish(new PcGamePlaySpendItem(slotIndex, team, ghostTeam, items));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Team);
        writer.WriteByte(GhostTeam);
        writer.WriteList(Items, static (list, item) =>
        {
            list.WriteUInt16(item.ItemId);
            list.WriteByte(item.Kind);
        });
    }

    /// <summary>Whether both spend the same items for the same character: every field equal, and the items equal in order.</summary>
    public bool Equals(PcGamePlaySpendItem? other) =>
        other is not null
        && SlotIndex == other.SlotIndex
        && Team == other.Team
        && GhostTeam == other.GhostTeam
        && Items.SequenceEqual(other.Items);

    /// <summary>A hash of every field and every item, consistent with <see cref="Equals(PcGamePlaySpendItem)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SlotIndex);
        hash.Add(Team);
        hash.Add(GhostTeam);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One item a shot spent.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Kind">How the item was spent, as the client numbers it.</param>
public readonly record struct SpentItem(ushort ItemId, byte Kind)
{
    /// <summary>Bytes one occupies.</summary>
    internal const int Size = sizeof(ushort) + sizeof(byte);
}
