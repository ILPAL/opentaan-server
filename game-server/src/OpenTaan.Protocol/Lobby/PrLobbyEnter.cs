using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The answer to <see cref="PqLobbyEnter"/>.</summary>
/// <param name="ChannelId">The channel whose lobby this is.</param>
/// <param name="Result">Whether the player entered. Only the values the enum names can be sent.</param>
[WireClass("PrLobby_Enter")]
public sealed record PrLobbyEnter(uint ChannelId, LobbyEnterResult Result) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrLobbyEnter>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrLobbyEnter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var channelId = reader.ReadUInt32();
        var result = (LobbyEnterResult)reader.ReadByte();
        return reader.Finish(new PrLobbyEnter(channelId, result));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Result"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Result);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(ChannelId);
        writer.WriteByte((byte)Result);
    }
}
