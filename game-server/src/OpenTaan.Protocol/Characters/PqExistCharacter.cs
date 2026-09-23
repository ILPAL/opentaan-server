using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>The client asks whether a character with this name exists.</summary>
/// <remarks>
/// Sent by the in-game "check name" button, not by character selection. Every
/// request must be answered, including for a name that does not exist: the client
/// disables the button until an answer arrives, for as long as the process runs.
/// </remarks>
/// <param name="CharacterName">The name to look up.</param>
[WireClass("PqExistCharacter")]
public sealed record PqExistCharacter(string CharacterName)
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqExistCharacter>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqExistCharacter> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterName = reader.ReadUtf16String();
        return reader.Finish(new PqExistCharacter(characterName));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUtf16String(CharacterName);
    }
}
