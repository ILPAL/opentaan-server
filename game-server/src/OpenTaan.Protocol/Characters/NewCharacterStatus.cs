namespace OpenTaan.Protocol.Characters;

/// <summary>The answer to <see cref="PqNewCharacter"/>.</summary>
/// <remarks>
/// The client has a dialog for each of these and nothing for any other value: the
/// creation screen then waits for good, with its buttons locked.
/// </remarks>
public enum NewCharacterStatus : byte
{
    /// <summary>The character was created; the client leaves the creation screen.</summary>
    Created = 0,

    /// <summary>The name is taken; the client asks for the name to be checked again.</summary>
    NameTaken = 1,

    /// <summary>Not created, for any other reason; the client reports an unknown error.</summary>
    Failed = 2,
}
