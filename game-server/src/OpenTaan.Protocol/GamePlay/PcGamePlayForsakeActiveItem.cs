using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>An item in use is taken off a character, its own or its team's.</summary>
/// <remarks>
/// Every client removes the item from the list it names when the server's copy comes
/// back, so it goes to everyone in the match, the sender included. The client looks the
/// character up by slot without checking the index, so the slot is always one of a room's,
/// and the team is a team number or 0xFF.
/// </remarks>
/// <param name="SlotIndex">The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Team">The character's team, or 0xFF for none.</param>
/// <param name="TargetIsEnemy">Not zero for an item aimed at the other team; kept as sent.</param>
/// <param name="TargetIsAlly">Not zero for an item aimed at the character's own team; kept as sent.</param>
/// <param name="ItemId">The item taken off.</param>
[WireClass("PcGamePlay_ForsakeActiveItem")]
public sealed record PcGamePlayForsakeActiveItem(byte SlotIndex, byte Team, byte TargetIsEnemy, byte TargetIsAlly, ushort ItemId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayForsakeActiveItem>.RegistryId;

    /// <summary>The slot of the character, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The character's team, or 0xFF for none.</summary>
    public byte Team { get; } = TeamNumber.IsTeamOrNone(Team)
        ? Team
        : throw new ArgumentOutOfRangeException(nameof(Team), $"Teams are numbered 0 to {PcGamePlayTurnStart.TeamCount - 1}, and {TeamNumber.None} names none.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayForsakeActiveItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var team = reader.ReadByte();
        var targetIsEnemy = reader.ReadByte();
        var targetIsAlly = reader.ReadByte();
        var itemId = reader.ReadUInt16();
        if (slotIndex >= GameSlotInfo.SlotCount || !TeamNumber.IsTeamOrNone(team))
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
            team = TeamNumber.None;
        }

        return reader.Finish(new PcGamePlayForsakeActiveItem(slotIndex, team, targetIsEnemy, targetIsAlly, itemId));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Team);
        writer.WriteByte(TargetIsEnemy);
        writer.WriteByte(TargetIsAlly);
        writer.WriteUInt16(ItemId);
    }
}
