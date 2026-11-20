using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A character uses an item, or gives it to a teammate: from its item bar, from what it holds, or as a step of a shot it plays back.</summary>
/// <remarks>
/// <para>
/// The player's client sends it and acts on it only when the server's copy comes back, so
/// it goes to everyone in the match, the sender included. The client looks the character
/// up by slot, and the team's lists by team, without checking either, so both are always
/// in range; an item slot it cannot find is skipped.
/// </para>
/// <para>
/// A player's client also lets it give one of its own items to the teammate whose turn it
/// is: a ghost's items to whoever has its team's turn, in any game mode, and in the modes
/// with captains a living character's items to its team's captain, on the captain's turn.
/// A gift names the giver's slot, and goes on the team's list it names instead of the
/// giver's own.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The slot of the character whose item it is, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Team">The character's team, one of the team numbers.</param>
/// <param name="GhostGift">
/// Not zero for a ghost's gift: the item goes on its team's list, which the team's next shot
/// uses. Kept as sent.
/// </param>
/// <param name="CaptainGift">
/// Not zero for a gift to the team's captain: the item goes on the team's captain's list,
/// which only a captain's shot uses. Kept as sent.
/// </param>
/// <param name="ItemSlot">The item bar slot the item comes from, or 0xFF for none.</param>
/// <param name="ItemId">The item.</param>
[WireClass("PcGamePlay_UseItem")]
public sealed record PcGamePlayUseItem(byte SlotIndex, byte Team, byte GhostGift, byte CaptainGift, byte ItemSlot, ushort ItemId) : IPacket
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
        var ghostGift = reader.ReadByte();
        var captainGift = reader.ReadByte();
        var itemSlot = reader.ReadByte();
        var itemId = reader.ReadUInt16();
        if (slotIndex >= GameSlotInfo.SlotCount || !TeamNumber.IsTeam(team))
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
            team = 0;
        }

        return reader.Finish(new PcGamePlayUseItem(slotIndex, team, ghostGift, captainGift, itemSlot, itemId));
    }

    /// <summary>Writes the body, as the client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Team);
        writer.WriteByte(GhostGift);
        writer.WriteByte(CaptainGift);
        writer.WriteByte(ItemSlot);
        writer.WriteUInt16(ItemId);
    }
}
