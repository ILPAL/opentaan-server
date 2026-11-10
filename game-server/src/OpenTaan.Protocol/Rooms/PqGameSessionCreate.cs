using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player asks to create a room.</summary>
/// <remarks>
/// The answer is the room itself, <see cref="PcGameSessionInit"/>, or a refusal,
/// <see cref="PrGameSessionCreateFail"/>. Until one of them arrives the client asks for
/// nothing else to do with rooms, so every request is answered. It never sends this from
/// inside a room.
/// </remarks>
/// <param name="Room">The room asked for, with <see cref="RoomStatus.Requested"/>; its number means nothing yet.</param>
/// <param name="Integrity">The client's report on its own code.</param>
[WireClass("PqGameSession_Create")]
public sealed record PqGameSessionCreate(GameSessionInfo Room, IntegrityReport Integrity) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionCreate>.RegistryId;

    /// <summary>The room asked for.</summary>
    public GameSessionInfo Room { get; } = Room ?? throw new ArgumentNullException(nameof(Room));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionCreate> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var room = GameSessionInfo.ReadFrom(ref reader);
        var integrity = IntegrityReport.ReadFrom(ref reader);
        return reader.Finish(new PqGameSessionCreate(room, integrity));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        Room.WriteTo(writer);
        Integrity.WriteTo(writer);
    }
}
