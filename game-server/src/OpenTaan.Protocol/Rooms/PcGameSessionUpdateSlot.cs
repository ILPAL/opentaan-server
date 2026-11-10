using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>One of a room's slots has changed: sent to the others in the room when a character takes a slot or leaves it.</summary>
/// <remarks>
/// The client finds the slot by its index in the room it was sent, and ignores a slot that
/// room did not list. When a slot is emptied of a character that is not its own, it
/// writes in the room's chat that the character has left. The team name is read only in
/// arena channels.
/// </remarks>
/// <param name="Change">Why the slot changed, which decides how the client draws it again.</param>
/// <param name="Slot">The slot as it is now.</param>
/// <param name="TeamName">The name of the slot's team, read only in arena channels.</param>
[WireClass("PcGameSession_UpdateSlot")]
public sealed record PcGameSessionUpdateSlot(SlotChange Change, GameSlotInfo Slot, string TeamName) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionUpdateSlot>.RegistryId;

    /// <summary>The slot as it is now.</summary>
    public GameSlotInfo Slot { get; } = Slot ?? throw new ArgumentNullException(nameof(Slot));

    /// <summary>The name of the slot's team, read only in arena channels.</summary>
    public string TeamName { get; } = TeamName ?? throw new ArgumentNullException(nameof(TeamName));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionUpdateSlot> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var change = (SlotChange)reader.ReadByte();
        var slot = GameSlotInfo.ReadFrom(ref reader);
        var teamName = reader.ReadUtf16String();
        return reader.Finish(new PcGameSessionUpdateSlot(change, slot, teamName));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Change"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Change);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Change);
        Slot.WriteTo(writer);
        writer.WriteUtf16String(TeamName);
    }
}
