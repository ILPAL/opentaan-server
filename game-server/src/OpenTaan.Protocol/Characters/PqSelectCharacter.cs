using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client picks the characters it will play, one for each of its accounts.</summary>
/// <param name="CharacterIds">
/// The characters picked, in the order of the accounts. Nothing here says they belong
/// to those accounts; a modified client can name any id.
/// </param>
[WireClass("PqSelectCharacter")]
public sealed record PqSelectCharacter(IReadOnlyList<uint> CharacterIds) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqSelectCharacter>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqSelectCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterIds = reader.ReadList(sizeof(uint), static (ref PacketReader element) => element.ReadUInt32());
        return reader.Finish(new PqSelectCharacter(characterIds));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(CharacterIds, static (list, characterId) => list.WriteUInt32(characterId));
    }
}
