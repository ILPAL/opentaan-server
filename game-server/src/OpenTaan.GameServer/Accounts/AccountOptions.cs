namespace OpenTaan.GameServer.Accounts;

/// <summary>Settings bound from the <c>Accounts</c> configuration section.</summary>
public sealed class AccountOptions
{
    public const string Section = "Accounts";

    /// <summary>
    /// Whether an account must be created before it can log in, with the password it
    /// was created with. When off, a login creates any account it names and accepts
    /// any password, which is convenient on a development machine and nowhere else:
    /// the server refuses to start with it off outside the Development environment.
    /// </summary>
    public bool RequireRegistration { get; set; } = true;

    /// <summary>
    /// Addresses of the accounts that log in as game masters, compared exactly. Only
    /// configuration makes an account a game master, never anything a client sends.
    /// </summary>
    /// <remarks>
    /// The flag opens the client's game master console and nothing else. Every game
    /// master request is still checked by the server when it arrives.
    /// </remarks>
    public IReadOnlyList<string> GameMasters { get; set; } = [];

    /// <summary>What a new account starts with.</summary>
    public AccountStart Start { get; set; } = new();
}

/// <summary>What a new account starts with.</summary>
public sealed class AccountStart
{
    /// <summary>Cash, the currency bought with real money.</summary>
    public uint Cash { get; set; }
}
