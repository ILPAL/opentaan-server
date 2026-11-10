using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>A player in the lobby asks who is in a room, before entering it.</summary>
/// <remarks>
/// The answer is <see cref="PrLobbyViewRoomSimpleInfo"/>. The client asks for no other
/// room until it is answered, so every request is answered.
/// </remarks>
/// <param name="RoomNumber">The room asked about.</param>
[WireClass("PqLobby_ViewRoomSimpleInfo")]
public sealed record PqLobbyViewRoomSimpleInfo(ushort RoomNumber) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqLobbyViewRoomSimpleInfo>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqLobbyViewRoomSimpleInfo> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var roomNumber = reader.ReadUInt16();
        return reader.Finish(new PqLobbyViewRoomSimpleInfo(roomNumber));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(RoomNumber);
    }
}
