using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player volunteers one of its characters as its team's captain.</summary>
/// <remarks>
/// The client offers it, while the room chooses its captains, only for the player's own
/// characters. It waits for no answer: the room is told with
/// <see cref="PcGameSessionChallengeChief"/>, and the player alone with
/// <see cref="PcGameSessionFailChallengeChief"/> when the server does not take it.
/// </remarks>
/// <param name="CharacterId">The character volunteering.</param>
[WireClass("PqGameSession_ChallengeChief")]
public sealed record PqGameSessionChallengeChief(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionChallengeChief>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionChallengeChief> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PqGameSessionChallengeChief(characterId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
