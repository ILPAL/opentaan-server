using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host of a room with captains asks for them to be chosen. It carries nothing but its id.</summary>
/// <remarks>
/// In a room playing <see cref="GameMode.CaptainItems"/> or <see cref="GameMode.CaptainNoItems"/>,
/// the host's Start sends this instead of a start, once every guest is ready. The room is
/// told with <see cref="PcGameSessionChiefDecision"/>, which opens the choice on every
/// screen; when it closes, the host's client asks for the start by itself.
/// </remarks>
[WireClass("PqGameSession_ChiefDecision")]
public sealed record PqGameSessionChiefDecision : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionChiefDecision>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionChiefDecision> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PqGameSessionChiefDecision());
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
