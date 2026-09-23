using System.Buffers;

namespace OpenTaan.Protocol.Handshake;

/// <summary>
/// The server's greeting, and the first frame on every connection: the three values
/// both ends derive the session's base seed from.
/// </summary>
/// <remarks>
/// It is the only packet that travels unencrypted, because neither end has a seed
/// until it arrives; every frame after it in either direction is encrypted. The
/// client sends nothing until it has read this, so the server speaks first.
///
/// The values are session key material. They are on the wire in the clear, so hiding
/// them in logs does not make the traffic confidential (threat model T-G4), but
/// <see cref="ToString"/> leaves them out anyway: key material is never logged.
/// </remarks>
/// <param name="Seed1">The first derivation value.</param>
/// <param name="Seed2">The second derivation value.</param>
/// <param name="Shift">How far both values are shifted before they are combined.</param>
[WireClass("PcFirstAccept")]
public sealed record PcFirstAccept(ulong Seed1, ulong Seed2, byte Shift)
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcFirstAccept>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcFirstAccept> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var seed1 = reader.ReadUInt64();
        var seed2 = reader.ReadUInt64();
        var shift = reader.ReadByte();
        return reader.Finish(new PcFirstAccept(seed1, seed2, shift));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt64(Seed1);
        writer.WriteUInt64(Seed2);
        writer.WriteByte(Shift);
    }

    /// <summary>Names the packet and its shift, never the seeds.</summary>
    public override string ToString() => $"{WireIdentity<PcFirstAccept>.Name} {{ Shift = {Shift} }}";
}
