using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>
/// The client sends a picture of one of its characters, as it draws it in the lobby. It
/// expects no answer.
/// </summary>
/// <remarks>
/// Sent on entering the lobby, once per character, after the character is selected and
/// again after its equipment changes. The picture is a PNG file.
/// </remarks>
/// <param name="CharacterId">The character in the picture.</param>
/// <param name="Image">The picture's bytes, or null when the client sent none.</param>
[WireClass("PcUploadWebImage")]
public sealed record PcUploadWebImage(uint CharacterId, byte[]? Image) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcUploadWebImage>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcUploadWebImage> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var image = reader.ReadBoolean() ? reader.ReadCountedBytes().ToArray() : null;
        return reader.Finish(new PcUploadWebImage(characterId, image));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteBoolean(Image is not null);
        if (Image is not null)
        {
            writer.WriteCountedBytes(Image);
        }
    }
}
