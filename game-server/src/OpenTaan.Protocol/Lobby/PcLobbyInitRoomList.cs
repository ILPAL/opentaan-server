using System.Buffers;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The rooms of the lobby the player just entered.</summary>
/// <remarks>
/// The client replaces the rooms it shows with these. It makes a new room of every entry
/// without looking at what kind it is, so a removal here would show as a room with
/// nothing in it: the list holds listings only, and one holding a removal can be neither
/// read nor written. A lobby with more rooms than one frame holds is sent this list with
/// as many as fit, and the rest as <see cref="PcLobbyUpdateRoomList"/>.
/// </remarks>
/// <param name="Rooms">Each room, as a listing.</param>
[WireClass("PcLobby_InitRoomList")]
public sealed record PcLobbyInitRoomList(IReadOnlyList<GameSessionInfoPack> Rooms) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLobbyInitRoomList>.RegistryId;

    /// <summary>Each room, as a listing.</summary>
    public IReadOnlyList<GameSessionInfoPack> Rooms { get; } = Rooms ?? throw new ArgumentNullException(nameof(Rooms));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcLobbyInitRoomList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var rooms = reader.ReadList(GameSessionInfoPack.MinimumSize, GameSessionInfoPack.ReadFrom);
        if (!ListsOnly(rooms))
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        return reader.Finish(new PcLobbyInitRoomList(rooms));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Rooms"/> holds a removal.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        if (!ListsOnly(Rooms))
        {
            throw new InvalidOperationException("The room list a lobby is entered with holds listings only.");
        }

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Rooms, static (list, room) => room.WriteTo(list));
    }

    private static bool ListsOnly(IReadOnlyList<GameSessionInfoPack> rooms) => rooms.All(room => room.Room is not null);
}
