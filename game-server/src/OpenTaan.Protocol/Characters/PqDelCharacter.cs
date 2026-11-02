using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client asks to delete one of an account's characters.</summary>
/// <remarks>
/// Sent from the character screen after the player confirms. The client's confirmation
/// dialog closes at once and the screen then waits for the answer with no time limit,
/// so every request is answered.
/// </remarks>
/// <param name="AccountSlot">
/// Which of the client's accounts the character belongs to: 0 or 1 from a real client,
/// anything from a modified one.
/// </param>
/// <param name="CharacterId">
/// The character to delete. Nothing here says it belongs to that account; a modified
/// client can name any id.
/// </param>
[WireClass("PqDelCharacter")]
public sealed record PqDelCharacter(byte AccountSlot, uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqDelCharacter>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqDelCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var accountSlot = reader.ReadByte();
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PqDelCharacter(accountSlot, characterId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(AccountSlot);
        writer.WriteUInt32(CharacterId);
    }
}
