namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqDelCharacter"/>.</summary>
/// <remarks>
/// The client has a dialog for each of these and nothing for any other value, and its
/// confirmation dialog has closed by then, so any other value looks like no answer.
/// </remarks>
public enum DeleteCharacterStatus : byte
{
    /// <summary>The character was deleted; the client redraws every account's characters.</summary>
    Deleted = 0,

    /// <summary>Not deleted, for a reason with no dialog of its own; the client reports an unknown error.</summary>
    Failed = 1,

    /// <summary>Not deleted: the character belongs to a guild.</summary>
    InGuild = 2,

    /// <summary>Not deleted: the character has a soul mate.</summary>
    HasSoulMate = 3,

    /// <summary>Not deleted: the character belongs to a guild and has a soul mate.</summary>
    InGuildAndHasSoulMate = 4,
}
