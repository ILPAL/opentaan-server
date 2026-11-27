using OpenTaan.GameServer.Matches;

namespace OpenTaan.GameServer.Characters;

/// <summary>Where characters are kept. Shared by every connection.</summary>
/// <remarks>
/// An account has at most one character: the client offers to create one only for an
/// account with none, and the store holds every caller to the same rule.
/// </remarks>
public interface ICharacterStore
{
    /// <summary>The characters of an account, oldest first. Empty when it has none.</summary>
    ValueTask<IReadOnlyList<Character>> FindByAccountAsync(uint accountId, CancellationToken cancellationToken);

    /// <summary>Whether a character already has this name, compared as <see cref="CharacterNames.Comparer"/> does.</summary>
    ValueTask<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a character for an account, in one step with the checks that its name is
    /// free and the account has no character, so of two creations at once at most one
    /// takes a name.
    /// </summary>
    /// <param name="accountId">The account the character is for.</param>
    /// <param name="name">The character's name. Its rules are checked by the caller.</param>
    /// <param name="build">Builds the character from the id the store gives it.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    ValueTask<CharacterCreation> CreateAsync(
        uint accountId,
        string name,
        Func<uint, Character> build,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a character, if it belongs to <paramref name="accountId"/>, and frees its
    /// name. Returns whether it was deleted.
    /// </summary>
    ValueTask<bool> DeleteAsync(uint accountId, uint characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a character finished the tutorial. Returns whether that is news:
    /// false when it was already recorded, or there is no such character.
    /// </summary>
    ValueTask<bool> FinishTutorialAsync(uint characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a match that has ended, and pays each character that started it what the
    /// record says: its experience and gold added, and the match counted as won or lost.
    /// Keeps each result-card offer with that payment, without crediting the card yet.
    /// </summary>
    /// <remarks>
    /// Each character is paid for a match once, however many times the match is kept and
    /// however many keep it at once, and all of it is paid or none. A character the store no
    /// longer has is not paid, and the rest still are.
    /// </remarks>
    /// <returns>The progress of each of the match's characters the store has, as it is kept now, by character id.</returns>
    ValueTask<IReadOnlyDictionary<uint, CharacterProgress>> KeepMatchAsync(MatchRecord match, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps a character's successful result-card pick and credits the offer kept with its
    /// match, in one step. A repeated pick credits nothing again.
    /// </summary>
    /// <remarks>
    /// The match must already have been kept, including its card offers. A card picked by
    /// another character in that match cannot be picked again. The character's wins and
    /// losses do not change. Its offer remains recorded if the character was deleted.
    /// </remarks>
    /// <returns>The character's progress as kept now, or null if it no longer exists.</returns>
    ValueTask<CharacterProgress?> KeepCardAsync(
        Guid matchId,
        uint characterId,
        byte cardIndex,
        CancellationToken cancellationToken);
}

/// <summary>How a creation ended.</summary>
/// <param name="Outcome">Whether the character was created, and if not, why.</param>
/// <param name="Character">The character created, or null.</param>
public sealed record CharacterCreation(CharacterCreationOutcome Outcome, Character? Character);

/// <summary>Whether a character was created, and if not, why.</summary>
public enum CharacterCreationOutcome
{
    /// <summary>The character was created.</summary>
    Created,

    /// <summary>Another character has the name.</summary>
    NameTaken,

    /// <summary>The account already has its character.</summary>
    AccountHasCharacter,
}
