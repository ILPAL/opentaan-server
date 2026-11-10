using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host of a room asks to send a player out of it.</summary>
/// <remarks>
/// The client names the character in the slot the host clicked, or
/// <see cref="NoCharacter"/> for a slot with none. It waits for no answer: the room is
/// told with <see cref="PcGameSessionUserExpel"/>.
/// </remarks>
/// <param name="CharacterId">The character in the slot clicked, or <see cref="NoCharacter"/>.</param>
[WireClass("PqGameSession_UserExpel")]
public sealed record PqGameSessionUserExpel(uint CharacterId) : IPacket
{
    /// <summary>What the client sends for a slot holding no character.</summary>
    public const uint NoCharacter = uint.MaxValue;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionUserExpel>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionUserExpel> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PqGameSessionUserExpel(characterId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
