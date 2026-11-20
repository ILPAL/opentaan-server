using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A room is choosing its captains: sent to everyone in it, its host included. It carries nothing but its id.</summary>
/// <remarks>
/// Every client opens the choice for five seconds: the characters of its player's own
/// team, each with a light, and for each of the player's own characters a button to
/// volunteer as captain (<see cref="PqGameSessionChallengeChief"/>) and one to withdraw
/// (<see cref="PqGameSessionCancelChief"/>). When the five seconds are over, the choice
/// closes, and the host's client asks for the start.
/// </remarks>
[WireClass("PcGameSession_ChiefDecision")]
public sealed record PcGameSessionChiefDecision : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionChiefDecision>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionChiefDecision> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PcGameSessionChiefDecision());
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
