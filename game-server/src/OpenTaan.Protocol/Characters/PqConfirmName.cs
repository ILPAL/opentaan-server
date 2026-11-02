using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client asks whether a name can be used for a new character.</summary>
/// <remarks>
/// Sent from the creation screen, which keeps its create button disabled until the
/// answer is <see cref="ConfirmNameStatus.Available"/>. The client checks the name
/// against its own rules first, but a modified client need not.
/// </remarks>
/// <param name="AccountSlot">
/// Which of the client's accounts the character would be for: 0 or 1 from a real
/// client, anything from a modified one. The answer does not carry it.
/// </param>
/// <param name="Name">The name to check.</param>
[WireClass("PqConfirmName")]
public sealed record PqConfirmName(byte AccountSlot, string Name) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqConfirmName>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqConfirmName> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var accountSlot = reader.ReadByte();
        var name = reader.ReadUtf16String();
        return reader.Finish(new PqConfirmName(accountSlot, name));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(AccountSlot);
        writer.WriteUtf16String(Name);
    }
}
