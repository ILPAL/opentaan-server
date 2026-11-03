using System.Buffers;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Protocol.Lobby;

/// <summary>A change to the lobby's player list: characters that came in, and characters that left.</summary>
/// <remarks>
/// <para>
/// The client adds every character listed without looking for one it already shows, so a
/// character added twice is shown twice; and it removes the first character it shows with
/// each id given. Adding each character once and removing it once is the server's part.
/// </para>
/// <para>
/// The characters added and their appearances are paired by position, as in
/// <see cref="PcLobbyInitUserList"/>, so the two lists are always the same length: an
/// update that is not cannot be built, and one received is refused as
/// <see cref="PacketError.UnexpectedValue"/>.
/// </para>
/// </remarks>
/// <param name="Characters">Each character added.</param>
/// <param name="RemovedCharacterIds">The characters that left.</param>
/// <param name="Appearances">How each character added looks, in the same order.</param>
[WireClass("PcLobby_UpdateUserList")]
public sealed record PcLobbyUpdateUserList(
    IReadOnlyList<CharacterInfo> Characters,
    IReadOnlyList<uint> RemovedCharacterIds,
    IReadOnlyList<CharacterAppear> Appearances) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLobbyUpdateUserList>.RegistryId;

    /// <summary>Each character added.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; } = Characters ?? throw new ArgumentNullException(nameof(Characters));

    /// <summary>The characters that left.</summary>
    public IReadOnlyList<uint> RemovedCharacterIds { get; } = RemovedCharacterIds ?? throw new ArgumentNullException(nameof(RemovedCharacterIds));

    /// <summary>How each character added looks, in the same order as <see cref="Characters"/>.</summary>
    public IReadOnlyList<CharacterAppear> Appearances { get; } = Appearances?.Count == Characters?.Count
        ? Appearances!
        : throw new ArgumentException("Every character added needs exactly one appearance, in the same order.", nameof(Appearances));

    /// <summary>An update that only adds characters.</summary>
    public static PcLobbyUpdateUserList Adding(IReadOnlyList<CharacterInfo> characters, IReadOnlyList<CharacterAppear> appearances) =>
        new(characters, [], appearances);

    /// <summary>An update that only removes characters.</summary>
    public static PcLobbyUpdateUserList Removing(IReadOnlyList<uint> characterIds) => new([], characterIds, []);

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcLobbyUpdateUserList> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characters = reader.ReadList(CharacterInfo.MinimumSize, CharacterInfo.ReadFrom);
        var removed = reader.ReadList(sizeof(uint), static (ref PacketReader element) => element.ReadUInt32());
        var appearances = reader.ReadList(CharacterAppear.Size, CharacterAppear.ReadFrom);

        // Also true after a failed read, which leaves its list empty.
        if (characters.Length != appearances.Length)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return reader.Finish(Removing([]));
        }

        return reader.Finish(new PcLobbyUpdateUserList(characters, removed, appearances));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Characters, static (list, character) => character.WriteTo(list));
        writer.WriteList(RemovedCharacterIds, static (list, characterId) => list.WriteUInt32(characterId));
        writer.WriteList(Appearances, static (list, appearance) => appearance.WriteTo(list));
    }
}
