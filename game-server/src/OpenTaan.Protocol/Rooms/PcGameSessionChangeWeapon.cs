using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The weapon a character takes into the match has changed: sent to everyone in the room.</summary>
/// <remarks>
/// The client finds the slot by the character's id, and ignores an id no slot of its room
/// carries.
/// </remarks>
/// <param name="CharacterId">The character.</param>
/// <param name="WeaponId">Its weapon now, from the client's own list of weapons.</param>
[WireClass("PcGameSession_ChangeWeapon")]
public sealed record PcGameSessionChangeWeapon(uint CharacterId, ushort WeaponId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionChangeWeapon>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionChangeWeapon> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var weaponId = reader.ReadUInt16();
        return reader.Finish(new PcGameSessionChangeWeapon(characterId, weaponId));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUInt16(WeaponId);
    }
}
