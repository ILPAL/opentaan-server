using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqExistCharacter"/>.</summary>
/// <remarks>
/// Send it only in reply, and once per request: every copy that arrives opens its own
/// dialog on the client.
/// </remarks>
/// <param name="Exists">Whether a character with that name exists.</param>
[WireClass("PrExistCharacter")]
public sealed record PrExistCharacter(bool Exists) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrExistCharacter>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrExistCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var exists = reader.ReadBoolean();
        return reader.Finish(new PrExistCharacter(exists));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteBoolean(Exists);
    }
}
