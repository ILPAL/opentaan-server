using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A character no longer volunteers as its team's captain: sent to everyone in the room, its player included.</summary>
/// <remarks>
/// A client showing the room's choice of captains puts the character's light out, if it is
/// on its player's team.
/// </remarks>
/// <param name="CharacterId">The character withdrawing.</param>
[WireClass("PcGameSession_CancelChief")]
public sealed record PcGameSessionCancelChief(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionCancelChief>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionCancelChief> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PcGameSessionCancelChief(characterId));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
