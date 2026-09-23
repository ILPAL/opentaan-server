namespace OpenTaan.Protocol.Accounts;

/// <summary>The answer to <see cref="PqCheckAccount"/>.</summary>
public enum CheckAccountStatus : byte
{
    /// <summary>The account exists; the client goes on to log in.</summary>
    Exists = 0,

    /// <summary>No such account; the client goes on to create one.</summary>
    NotFound = 1,
}
