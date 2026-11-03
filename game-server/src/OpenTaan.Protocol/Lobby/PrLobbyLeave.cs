using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The answer to <see cref="PqLobbyLeave"/>. Only this lets the client enter a lobby again later.</summary>
[WireClass("PrLobby_Leave")]
public sealed record PrLobbyLeave : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrLobbyLeave>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrLobbyLeave> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PrLobbyLeave());
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
