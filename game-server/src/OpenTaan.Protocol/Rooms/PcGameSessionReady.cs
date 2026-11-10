using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player is ready to start, or no longer is: sent to everyone in the room.</summary>
/// <remarks>
/// The client marks every occupied slot carrying the player's id, so both characters of
/// a connection logged in with two accounts change together.
/// </remarks>
/// <param name="PlayerId">The player's id, as its slots carry it.</param>
/// <param name="IsReady">Whether the player is ready.</param>
[WireClass("PcGameSession_Ready")]
public sealed record PcGameSessionReady(uint PlayerId, bool IsReady) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionReady>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionReady> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var playerId = reader.ReadUInt32();
        var isReady = reader.ReadBoolean();
        return reader.Finish(new PcGameSessionReady(playerId, isReady));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(PlayerId);
        writer.WriteBoolean(IsReady);
    }
}
