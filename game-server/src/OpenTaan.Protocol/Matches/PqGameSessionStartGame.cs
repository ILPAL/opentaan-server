using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>The host of a room asks to start its match. It carries nothing but its id.</summary>
/// <remarks>
/// Only the host's client sends it, and it waits on no answer: it keeps no lock, only a
/// limit of one request every two seconds. So a start the server refuses is not answered,
/// and the host may simply ask again. A start that goes ahead is answered, to everyone in
/// the room, by <see cref="PcGameSessionStartGame"/>.
/// </remarks>
[WireClass("PqGameSession_StartGame")]
public sealed record PqGameSessionStartGame : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionStartGame>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionStartGame> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PqGameSessionStartGame());
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
