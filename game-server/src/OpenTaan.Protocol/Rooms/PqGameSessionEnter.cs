using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>A player asks to enter a room, from the lobby's room list or by accepting an invitation.</summary>
/// <remarks>
/// The answer is the room, <see cref="PcGameSessionInit"/>, or a refusal,
/// <see cref="PrGameSessionEnterFail"/>. Until one of them arrives the client asks for
/// nothing else to do with rooms, so every request is answered. The client checks no
/// password itself: it sends what was typed, or the invitation's.
/// </remarks>
/// <param name="RoomNumber">The room asked for.</param>
/// <param name="Password">The password given, or empty for a room the client believes has none.</param>
/// <param name="Integrity">The client's report on its own code.</param>
[WireClass("PqGameSession_Enter")]
public sealed record PqGameSessionEnter(ushort RoomNumber, string Password, IntegrityReport Integrity) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionEnter>.RegistryId;

    /// <summary>The password given, or empty.</summary>
    public string Password { get; } = Password ?? throw new ArgumentNullException(nameof(Password));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionEnter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var roomNumber = reader.ReadUInt16();
        var password = reader.ReadUtf16String();
        var integrity = IntegrityReport.ReadFrom(ref reader);
        return reader.Finish(new PqGameSessionEnter(roomNumber, password, integrity));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(RoomNumber);
        writer.WriteUtf16String(Password);
        Integrity.WriteTo(writer);
    }
}
