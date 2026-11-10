using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player in a room asks to be ready to start, or no longer to be.</summary>
/// <remarks>
/// The client asks for the opposite of what its own slot shows. The host has no Ready
/// button, since it starts the match instead. The client waits for no answer: the room is
/// told the change with <see cref="PcGameSessionReady"/>.
/// </remarks>
/// <param name="IsReady">Whether the player asks to be ready.</param>
[WireClass("PqGameSession_Ready")]
public sealed record PqGameSessionReady(bool IsReady) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionReady>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionReady> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var isReady = reader.ReadBoolean();
        return reader.Finish(new PqGameSessionReady(isReady));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteBoolean(IsReady);
    }
}
