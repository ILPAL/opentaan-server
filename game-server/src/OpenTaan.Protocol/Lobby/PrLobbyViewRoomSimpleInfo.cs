using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The answer to <see cref="PqLobbyViewRoomSimpleInfo"/>: the characters in the room, or that it is gone.</summary>
/// <param name="Result">Whether the room's players follow. Only the values the enum names can be written.</param>
/// <param name="Players">The characters in the room; the client reads them only when they are listed.</param>
[WireClass("PrLobby_ViewRoomSimpleInfo")]
public sealed record PrLobbyViewRoomSimpleInfo(RoomInfoResult Result, IReadOnlyList<UserSimpleInfo> Players) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrLobbyViewRoomSimpleInfo>.RegistryId;

    /// <summary>The characters in the room.</summary>
    public IReadOnlyList<UserSimpleInfo> Players { get; } = Players ?? throw new ArgumentNullException(nameof(Players));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrLobbyViewRoomSimpleInfo> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var result = (RoomInfoResult)reader.ReadByte();
        var players = reader.ReadList(UserSimpleInfo.MinimumSize, UserSimpleInfo.ReadFrom);
        return reader.Finish(new PrLobbyViewRoomSimpleInfo(result, players));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Result"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Result);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Result);
        writer.WriteList(Players, static (list, player) => player.WriteTo(list));
    }
}
