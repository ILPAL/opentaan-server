using System.Buffers;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Protocol.Lobby;

/// <summary>The characters in the lobby the player just entered: each one's description and how it looks.</summary>
/// <remarks>
/// The client pairs the two lists by position, as it does a character catalog, so they
/// are always the same length: a list that is not cannot be built, and one received is
/// refused as <see cref="PacketError.UnexpectedValue"/>.
/// </remarks>
/// <param name="Characters">Each character's description.</param>
/// <param name="Appearances">How each character looks, in the same order.</param>
[WireClass("PcLobby_InitUserList")]
public sealed record PcLobbyInitUserList(IReadOnlyList<CharacterInfo> Characters, IReadOnlyList<CharacterAppear> Appearances) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLobbyInitUserList>.RegistryId;

    /// <summary>Each character's description.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; } = Characters ?? throw new ArgumentNullException(nameof(Characters));

    /// <summary>How each character looks, in the same order as <see cref="Characters"/>.</summary>
    public IReadOnlyList<CharacterAppear> Appearances { get; } = Appearances?.Count == Characters?.Count
        ? Appearances!
        : throw new ArgumentException("Every character needs exactly one appearance, in the same order.", nameof(Appearances));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcLobbyInitUserList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characters = reader.ReadList(CharacterInfo.MinimumSize, CharacterInfo.ReadFrom);
        var appearances = reader.ReadList(CharacterAppear.Size, CharacterAppear.ReadFrom);

        // Also true after a failed read, which leaves its list empty.
        if (characters.Length != appearances.Length)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return reader.Finish(new PcLobbyInitUserList([], []));
        }

        return reader.Finish(new PcLobbyInitUserList(characters, appearances));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Characters, static (list, character) => character.WriteTo(list));
        writer.WriteList(Appearances, static (list, appearance) => appearance.WriteTo(list));
    }
}
