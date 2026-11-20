using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player withdraws one of its characters from volunteering as its team's captain.</summary>
/// <remarks>
/// The client offers it, while the room chooses its captains, only for the player's own
/// characters. It waits for no answer: the room is told with
/// <see cref="PcGameSessionCancelChief"/>.
/// </remarks>
/// <param name="CharacterId">The character withdrawing.</param>
[WireClass("PqGameSession_CancelChief")]
public sealed record PqGameSessionCancelChief(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionCancelChief>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionCancelChief> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PqGameSessionCancelChief(characterId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
