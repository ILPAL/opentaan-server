using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.GameServer.Characters;

/// <summary>Character requests.</summary>
public sealed class CharacterHandlers
{
    /// <summary>
    /// Answers whether a character with the name exists. No character exists until
    /// characters can be created, so the answer is always no.
    /// </summary>
    /// <remarks>
    /// Answered in any state and every time: the client disables its "check name"
    /// button until an answer arrives, for as long as the process runs.
    /// </remarks>
    public ValueTask ExistCharacterAsync(ClientSession session, PqExistCharacter request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.Send(new PrExistCharacter(Exists: false));
        return ValueTask.CompletedTask;
    }
}
