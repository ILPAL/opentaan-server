using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A character has volunteered as its team's captain: sent to everyone in the room, its player included.</summary>
/// <remarks>
/// A client showing the room's choice of captains lights the character up, if it is on its
/// player's team.
/// </remarks>
/// <param name="CharacterId">The character volunteering.</param>
[WireClass("PcGameSession_ChallengeChief")]
public sealed record PcGameSessionChallengeChief(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionChallengeChief>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionChallengeChief> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PcGameSessionChallengeChief(characterId));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
