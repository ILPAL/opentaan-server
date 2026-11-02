namespace OpenTaan.Protocol.Characters;

/// <summary>
/// An account's characters, as login and character creation and deletion carry them:
/// each character's description, and how each one looks.
/// </summary>
/// <remarks>
/// The client pairs the two lists by position, the first appearance with the first
/// character and so on, and reads the appearance at a character's position without
/// checking that there is one. So the lists are always the same length: a catalog
/// that is not cannot be built, and one received is refused as
/// <see cref="PacketError.UnexpectedValue"/>.
/// </remarks>
/// <param name="Characters">Each character's description.</param>
/// <param name="Appearances">How each character looks, in the same order.</param>
[WireClass("CharacterCatalog")]
public sealed record CharacterCatalog(IReadOnlyList<CharacterInfo> Characters, IReadOnlyList<CharacterAppear> Appearances)
{
    /// <summary>The catalog of an account with no characters.</summary>
    public static CharacterCatalog Empty { get; } = new([], []);

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<CharacterCatalog>.RegistryId;

    /// <summary>The fewest bytes it can occupy: its id and two empty lists.</summary>
    internal const int MinimumSize = 3 * sizeof(uint);

    /// <summary>Each character's description.</summary>
    public IReadOnlyList<CharacterInfo> Characters { get; } = Characters ?? throw new ArgumentNullException(nameof(Characters));

    /// <summary>How each character looks, in the same order as <see cref="Characters"/>.</summary>
    public IReadOnlyList<CharacterAppear> Appearances { get; } = Paired(Characters, Appearances);

    internal static CharacterCatalog ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var characters = reader.ReadList(CharacterInfo.MinimumSize, CharacterInfo.ReadFrom);
        var appearances = reader.ReadList(CharacterAppear.Size, CharacterAppear.ReadFrom);

        // Also true after a failed read, which leaves its list empty, so the catalog is
        // never built from lists that cannot be paired.
        if (characters.Length != appearances.Length)
        {
            reader.Fail(PacketError.UnexpectedValue);
            return Empty;
        }

        return new CharacterCatalog(characters, appearances);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Characters, static (list, character) => character.WriteTo(list));
        writer.WriteList(Appearances, static (list, appearance) => appearance.WriteTo(list));
    }

    private static IReadOnlyList<CharacterAppear> Paired(
        IReadOnlyList<CharacterInfo> characters,
        IReadOnlyList<CharacterAppear> appearances)
    {
        ArgumentNullException.ThrowIfNull(appearances);

        return appearances.Count == characters?.Count
            ? appearances
            : throw new ArgumentException("Every character needs exactly one appearance, in the same order.", nameof(Appearances));
    }
}
