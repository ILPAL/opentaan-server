using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqNewCharacter"/>.</summary>
/// <remarks>
/// The client waits for this with no time limit and its buttons locked, so every
/// request is answered.
/// </remarks>
/// <param name="Status">Whether the character was created. Only the values the enum names can be sent.</param>
/// <param name="AccountSlot">
/// The slot the character was created for. The client stores <paramref name="Catalog"/>
/// at this slot without checking it, so it must be one the connection holds.
/// </param>
/// <param name="Catalog">The account's characters after the change, or null when nothing changed.</param>
[WireClass("PrNewCharacter")]
public sealed record PrNewCharacter(NewCharacterStatus Status, byte AccountSlot, CharacterCatalog? Catalog) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrNewCharacter>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrNewCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var (status, accountSlot, catalog) = CharacterChangeReply.ReadFields(ref reader);
        return reader.Finish(new PrNewCharacter((NewCharacterStatus)status, accountSlot, catalog));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Status"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        DialogStatus.EnsureKnown(Status);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        CharacterChangeReply.WriteFields(writer, (byte)Status, AccountSlot, Catalog);
    }
}
