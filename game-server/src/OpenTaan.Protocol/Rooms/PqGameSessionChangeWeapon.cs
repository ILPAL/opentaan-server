using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player in a room picks the weapon one of its characters takes into the match.</summary>
/// <remarks>
/// The client's weapon dialog lists the player's own characters, and sends one request for
/// each whose weapon it changed. The weapons it offers come from the client's own files. It
/// waits for no answer: the room is told the change with <see cref="PcGameSessionChangeWeapon"/>.
/// </remarks>
/// <param name="CharacterId">The character whose weapon changes.</param>
/// <param name="WeaponId">The weapon picked, from the client's own list of weapons.</param>
[WireClass("PqGameSession_ChangeWeapon")]
public sealed record PqGameSessionChangeWeapon(uint CharacterId, ushort WeaponId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionChangeWeapon>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionChangeWeapon> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var weaponId = reader.ReadUInt16();
        return reader.Finish(new PqGameSessionChangeWeapon(characterId, weaponId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUInt16(WeaponId);
    }
}
