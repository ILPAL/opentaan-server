using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The client asks to enter its channel's lobby, when its lobby screen opens.</summary>
/// <remarks>The channel is the one the client last moved to; the request does not name it.</remarks>
/// <param name="FromChannelMove">Whether the client arrives from a channel move it reconnected for.</param>
/// <param name="ArenaTeamMode">The arena team mode, or 0xFF outside an arena channel.</param>
/// <param name="ArenaTeamKey">The arena team, or 0xFFFFFFFF outside an arena channel.</param>
[WireClass("PqLobby_Enter")]
public sealed record PqLobbyEnter(byte FromChannelMove, byte ArenaTeamMode, uint ArenaTeamKey) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqLobbyEnter>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqLobbyEnter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var fromChannelMove = reader.ReadByte();
        var arenaTeamMode = reader.ReadByte();
        var arenaTeamKey = reader.ReadUInt32();
        return reader.Finish(new PqLobbyEnter(fromChannelMove, arenaTeamMode, arenaTeamKey));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(FromChannelMove);
        writer.WriteByte(ArenaTeamMode);
        writer.WriteUInt32(ArenaTeamKey);
    }
}
