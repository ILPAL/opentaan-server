using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>Changes to the lobby's room list: rooms listed again, or removed.</summary>
/// <remarks>
/// The client finds each entry by its room number. It applies a listing to the room it
/// shows with that number, or adds the room when it shows none, so a room listed twice
/// is shown once; and it drops the room a removal names, if it shows it.
/// </remarks>
/// <param name="Changes">Each change, in the order the client applies them.</param>
[WireClass("PcLobby_UpdateRoomList")]
public sealed record PcLobbyUpdateRoomList(IReadOnlyList<GameSessionInfoPack> Changes) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLobbyUpdateRoomList>.RegistryId;

    /// <summary>Each change, in the order the client applies them.</summary>
    public IReadOnlyList<GameSessionInfoPack> Changes { get; } = Changes ?? throw new ArgumentNullException(nameof(Changes));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcLobbyUpdateRoomList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var changes = reader.ReadList(GameSessionInfoPack.MinimumSize, GameSessionInfoPack.ReadFrom);
        return reader.Finish(new PcLobbyUpdateRoomList(changes));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Changes, static (list, change) => change.WriteTo(list));
    }
}
