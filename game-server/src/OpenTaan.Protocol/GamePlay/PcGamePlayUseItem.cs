using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character uses an item: from its item bar, from what it holds, or as a step of a shot it plays back.</summary>
/// <remarks>
/// The player's client sends it and acts on it only when the server's copy comes back, so
/// it goes to everyone in the match, the sender included. The client looks the character
/// up by slot, and the team's lists by team, without checking either, so both are always
/// in range; an item slot it cannot find is skipped.
/// </remarks>
/// <param name="SlotIndex">The slot of the character using the item, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Team">The character's team, one of the team numbers.</param>
/// <param name="TargetIsEnemy">Not zero for an item aimed at the other team; kept as sent.</param>
/// <param name="TargetIsAlly">Not zero for an item aimed at the character's own team; kept as sent.</param>
/// <param name="ItemSlot">The item bar slot the item comes from, or 0xFF for none.</param>
/// <param name="ItemId">The item.</param>
[WireClass("PcGamePlay_UseItem")]
public sealed record PcGamePlayUseItem(byte SlotIndex, byte Team, byte TargetIsEnemy, byte TargetIsAlly, byte ItemSlot, ushort ItemId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayUseItem>.RegistryId;

    /// <summary>The slot of the character using the item, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The character's team, one of the team numbers.</summary>
    public byte Team { get; } = TeamNumber.IsTeam(Team)
        ? Team
        : throw new ArgumentOutOfRangeException(nameof(Team), $"Teams are numbered 0 to {PcGamePlayTurnStart.TeamCount - 1}.");

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayUseItem> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var team = reader.ReadByte();
        var targetIsEnemy = reader.ReadByte();
        var targetIsAlly = reader.ReadByte();
        var itemSlot = reader.ReadByte();
        var itemId = reader.ReadUInt16();
        if (slotIndex >= GameSlotInfo.SlotCount || !TeamNumber.IsTeam(team))
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
            team = 0;
        }

        return reader.Finish(new PcGamePlayUseItem(slotIndex, team, targetIsEnemy, targetIsAlly, itemSlot, itemId));
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
        writer.WriteByte(ItemSlot);
        writer.WriteUInt16(ItemId);
    }
}
