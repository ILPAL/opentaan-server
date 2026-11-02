using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqDelCharacter"/>.</summary>
/// <remarks>
/// The client waits for this with no time limit, and acts on it only while it is on
/// its character screen, so it is answered at once.
/// </remarks>
/// <param name="Status">Whether the character was deleted. Only the values the enum names can be sent.</param>
/// <param name="AccountSlot">
/// The slot the request named. The client stores <paramref name="Catalog"/> at this
/// slot without checking it, so it must be one the connection holds.
/// </param>
/// <param name="Catalog">
/// The account's characters after the deletion, or null when nothing changed. A
/// deletion must carry it: it is the only way the client learns what is left.
/// </param>
[WireClass("PrDelCharacter")]
public sealed record PrDelCharacter(DeleteCharacterStatus Status, byte AccountSlot, CharacterCatalog? Catalog) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrDelCharacter>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrDelCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var (status, accountSlot, catalog) = CharacterChangeReply.ReadFields(ref reader);
        return reader.Finish(new PrDelCharacter((DeleteCharacterStatus)status, accountSlot, catalog));
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
