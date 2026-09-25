namespace OpenTaan.Protocol.Accounts;

/// <summary>Why a login was refused. Each value selects the message the player sees.</summary>
public enum LoginRefusalReason : uint
{
    /// <summary>
    /// A general login error. Never send it: the client does not return to the login
    /// form after this one, it closes the game two seconds later.
    /// </summary>
    LoginFailed = 0,

    /// <summary>No such account.</summary>
    UnknownAccount = 1,

    /// <summary>The account is already in the game.</summary>
    AlreadyInGame = 2,

    /// <summary>No such account; shown with the same message as <see cref="UnknownAccount"/>.</summary>
    UnknownAccountAlternate = 3,

    /// <summary>The account is under a penalty; the message is formatted with the refusal's two detail values.</summary>
    Penalty = 5,

    /// <summary>The password is wrong.</summary>
    WrongPassword = 7,
}
