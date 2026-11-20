using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A character's volunteering as captain was not taken: sent to its player alone.</summary>
/// <remarks>
/// The client changes nothing on the screen for it: the character's light stays out.
/// </remarks>
/// <param name="CharacterId">The character that volunteered.</param>
[WireClass("PcGameSession_FailChallengeChief")]
public sealed record PcGameSessionFailChallengeChief(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionFailChallengeChief>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionFailChallengeChief> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PcGameSessionFailChallengeChief(characterId));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
