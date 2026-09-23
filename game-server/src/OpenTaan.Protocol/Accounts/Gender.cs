namespace OpenTaan.Protocol.Accounts;

/// <summary>An account's gender, which decides the appearance options the character creator offers.</summary>
public enum Gender : byte
{
    /// <summary>
    /// Not chosen. The client never sends it, and an account sent to the client with it
    /// gets a character creator with no hair, eye, mouth or clothing options at all.
    /// </summary>
    Unspecified = 0,

    /// <summary>Male.</summary>
    Male = 1,

    /// <summary>Female.</summary>
    Female = 2,
}
