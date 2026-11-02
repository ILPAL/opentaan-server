using System.Buffers;
using OpenTaan.Protocol.Items;

namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqSelectCharacter"/>: everything the client needs to start playing.</summary>
/// <remarks>
/// Each list has one entry per selected character, in the order of the request. After
/// this reply the client uses the same connection to play.
/// </remarks>
/// <param name="Characters">The selected characters.</param>
/// <param name="ItemInventories">Each character's item inventory.</param>
/// <param name="EquipmentInventories">Each character's equipment inventory.</param>
/// <param name="BingoInventories">Each character's bingo inventory.</param>
/// <param name="TutorialFlags">
/// 0 offers the tutorial, anything else skips it. The client reads only the first, and
/// offers the tutorial only to a character below level 2.
/// </param>
/// <param name="SlotUnlockFlags">Whether each slot is unlocked; 0 locks it.</param>
[WireClass("PrSelectCharacter")]
public sealed record PrSelectCharacter(
    IReadOnlyList<CharacterInfo> Characters,
    IReadOnlyList<Inventory> ItemInventories,
    IReadOnlyList<Inventory> EquipmentInventories,
    IReadOnlyList<Inventory> BingoInventories,
    IReadOnlyList<byte> TutorialFlags,
    IReadOnlyList<byte> SlotUnlockFlags) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrSelectCharacter>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrSelectCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characters = reader.ReadList(CharacterInfo.MinimumSize, CharacterInfo.ReadFrom);
        var itemInventories = reader.ReadList(Inventory.MinimumSize, Inventory.ReadFrom);
        var equipmentInventories = reader.ReadList(Inventory.MinimumSize, Inventory.ReadFrom);
        var bingoInventories = reader.ReadList(Inventory.MinimumSize, Inventory.ReadFrom);
        var tutorialFlags = reader.ReadList(sizeof(byte), static (ref PacketReader element) => element.ReadByte());
        var slotUnlockFlags = reader.ReadList(sizeof(byte), static (ref PacketReader element) => element.ReadByte());
        return reader.Finish(new PrSelectCharacter(
            characters, itemInventories, equipmentInventories, bingoInventories, tutorialFlags, slotUnlockFlags));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Characters, static (list, character) => character.WriteTo(list));
        writer.WriteList(ItemInventories, static (list, inventory) => inventory.WriteTo(list));
        writer.WriteList(EquipmentInventories, static (list, inventory) => inventory.WriteTo(list));
        writer.WriteList(BingoInventories, static (list, inventory) => inventory.WriteTo(list));
        writer.WriteList(TutorialFlags, static (list, flag) => list.WriteByte(flag));
        writer.WriteList(SlotUnlockFlags, static (list, flag) => list.WriteByte(flag));
    }
}
