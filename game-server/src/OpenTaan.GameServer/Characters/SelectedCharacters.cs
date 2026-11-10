using OpenTaan.GameServer.Sessions;

namespace OpenTaan.GameServer.Characters;

/// <summary>Reads the characters a connection plays.</summary>
public static class SelectedCharacters
{
    /// <summary>
    /// The characters <paramref name="session"/> has selected, each read from the account
    /// in its slot, in slot order.
    /// </summary>
    /// <remarks>
    /// A character the store no longer has is left out, so the list can be shorter than the
    /// selection; callers that need every one of them compare the two.
    /// </remarks>
    public static async ValueTask<IReadOnlyList<Character>> ReadAsync(
        ICharacterStore store,
        ClientSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(session);

        var playing = new List<Character>(session.SelectedCharacterIds.Count);
        for (var slot = 0; slot < session.SelectedCharacterIds.Count; slot++)
        {
            var owned = await store.FindByAccountAsync(session.Accounts[slot].AccountId, cancellationToken).ConfigureAwait(false);
            if (owned.FirstOrDefault(character => character.CharacterId == session.SelectedCharacterIds[slot]) is { } character)
            {
                playing.Add(character);
            }
        }

        return playing;
    }
}
