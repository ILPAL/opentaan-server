using System.Buffers;

namespace OpenTaan.Protocol.Rooms;

/// <summary>The host has sent a player out of the room: sent to everyone in it, that player included.</summary>
/// <remarks>
/// <para>
/// The client looks for a slot carrying the player's id. The client of that player then
/// says it was sent out, and asks at once to leave the room. A player's id, not one of its
/// characters', is what makes this reach it: an id no slot carries changes nothing.
/// </para>
/// <para>
/// The two strings ahead of the id are read and never used. This server sends them empty.
/// </para>
/// </remarks>
/// <param name="Field10">Read by the client and never used. Named after where the client keeps it.</param>
/// <param name="Field14">Read by the client and never used. Named after where the client keeps it.</param>
/// <param name="PlayerId">The player sent out, as its slots carry it.</param>
[WireClass("PcGameSession_UserExpel")]
public sealed record PcGameSessionUserExpel(string Field10, string Field14, uint PlayerId) : IPacket
{
    /// <summary>Player <paramref name="playerId"/> sent out, with the two unused strings empty.</summary>
    public PcGameSessionUserExpel(uint playerId)
        : this(string.Empty, string.Empty, playerId)
    {
    }

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionUserExpel>.RegistryId;

    /// <summary>Read by the client and never used.</summary>
    public string Field10 { get; } = Field10 ?? throw new ArgumentNullException(nameof(Field10));

    /// <summary>Read by the client and never used.</summary>
    public string Field14 { get; } = Field14 ?? throw new ArgumentNullException(nameof(Field14));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionUserExpel> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var field10 = reader.ReadUtf16String();
        var field14 = reader.ReadUtf16String();
        var playerId = reader.ReadUInt32();
        return reader.Finish(new PcGameSessionUserExpel(field10, field14, playerId));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUtf16String(Field10);
        writer.WriteUtf16String(Field14);
        writer.WriteUInt32(PlayerId);
    }
}
