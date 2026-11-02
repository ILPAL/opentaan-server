using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The client leaves its lobby, as it moves on to a room. It carries nothing but its id.</summary>
[WireClass("PqLobby_Leave")]
public sealed record PqLobbyLeave : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqLobbyLeave>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqLobbyLeave> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PqLobbyLeave());
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
