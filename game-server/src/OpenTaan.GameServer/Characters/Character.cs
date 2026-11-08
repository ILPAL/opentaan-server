using OpenTaan.Protocol.Characters;

namespace OpenTaan.GameServer.Characters;

/// <summary>A character, and the account it belongs to.</summary>
/// <remarks>
/// Held in the shape the client is sent until the game state stage gives characters a
/// model of their own, so that what the server knows about a character and what it
/// tells the client cannot drift apart.
/// </remarks>
/// <param name="AccountId">The account the character belongs to.</param>
/// <param name="Info">The character's name, progress and record.</param>
/// <param name="Appearance">How the character looks.</param>
public sealed record Character(uint AccountId, CharacterInfo Info, CharacterAppear Appearance)
{
    /// <summary>The character's id.</summary>
    public uint CharacterId => Info.CharacterId;

    /// <summary>The character's name.</summary>
    public string Name => Info.Name;

    /// <summary>
    /// Whether the player reached the end of the tutorial with this character. The
    /// client keeps no record of it, so this is the only thing that stops the tutorial
    /// coming back at every selection.
    /// </summary>
    public bool HasFinishedTutorial { get; init; }

    /// <summary>An account's characters as the client is sent them, each with its appearance in the same position.</summary>
    public static CharacterCatalog CatalogOf(IReadOnlyCollection<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);

        return new CharacterCatalog(
            [.. characters.Select(character => character.Info)],
            [.. characters.Select(character => character.Appearance)]);
    }
}
