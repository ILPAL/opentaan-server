using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client asks to create a character for one of its accounts.</summary>
/// <remarks>
/// A real client sends this only after <see cref="PqConfirmName"/> was answered
/// available, and only for an account with no character. Neither is evidence: a
/// modified client sends what it likes, and a late answer to an earlier check can
/// leave a different name in the box.
/// </remarks>
/// <param name="AccountSlot">
/// Which of the client's accounts the character is for: 0 or 1 from a real client,
/// anything from a modified one.
/// </param>
/// <param name="Name">The character's name.</param>
/// <param name="Appearance">The parts chosen, exactly <see cref="AppearanceSlot.Count"/> of them.</param>
[WireClass("PqNewCharacter")]
public sealed record PqNewCharacter(byte AccountSlot, string Name, IReadOnlyList<AppearanceSlot> Appearance) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqNewCharacter>.RegistryId;

    /// <summary>The parts chosen, exactly <see cref="AppearanceSlot.Count"/> of them.</summary>
    public IReadOnlyList<AppearanceSlot> Appearance { get; } = AppearanceSlot.Complete(Appearance, nameof(Appearance));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqNewCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var accountSlot = reader.ReadByte();
        var name = reader.ReadUtf16String();
        var appearance = AppearanceSlot.ReadAll(ref reader);
        return reader.Finish(new PqNewCharacter(accountSlot, name, appearance));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(AccountSlot);
        writer.WriteUtf16String(Name);
        AppearanceSlot.WriteAll(writer, Appearance);
    }
}
