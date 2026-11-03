using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The rooms of the lobby the player just entered.</summary>
/// <remarks>
/// Only the empty list is supported so far: rooms arrive with the room packets. Until
/// then a list that holds anything is refused as <see cref="PacketError.Unsupported"/>
/// rather than skipped, because a room's size cannot be known without reading it.
/// </remarks>
[WireClass("PcLobby_InitRoomList")]
public sealed record PcLobbyInitRoomList : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLobbyInitRoomList>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcLobbyInitRoomList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        if (reader.ReadUInt32() != 0)
        {
            reader.Fail(PacketError.Unsupported);
        }

        return reader.Finish(new PcLobbyInitRoomList());
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(0);
    }
}
