namespace OpenTaan.Protocol.Accounts;

/// <summary>The answer to <see cref="PqCreateAccount"/>.</summary>
/// <remarks>Any other value shows the client's "unknown error" message and changes nothing.</remarks>
public enum CreateAccountStatus : byte
{
    /// <summary>The account was created.</summary>
    Created = 0,

    /// <summary>The account already exists; the client shows the login dialog again.</summary>
    AlreadyExists = 1,
}
