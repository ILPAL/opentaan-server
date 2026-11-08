using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client reports that the player reached the end of the tutorial.</summary>
/// <remarks>
/// <para>
/// The client plays the tutorial alone, sends this as it ends, and moves on to the
/// lobby without waiting: nothing answers it. It keeps no record of its own, so the
/// tutorial comes back at every selection until the server records it as finished and
/// says so in <see cref="PrSelectCharacter.TutorialFlags"/>.
/// </para>
/// <para>
/// <see cref="CharacterId"/> is not to be trusted to name the character that played:
/// the client fills it with the first character selected since it started, even after
/// a later selection, and nothing checks it.
/// </para>
/// </remarks>
/// <param name="CharacterId">A character id, as the client remembers one. See the remarks.</param>
[WireClass("PcTutorialFinished")]
public sealed record PcTutorialFinished(uint CharacterId) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcTutorialFinished>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcTutorialFinished> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        return reader.Finish(new PcTutorialFinished(characterId));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
    }
}
