using Microsoft.Extensions.Options;

namespace OpenTaan.GameServer.Accounts;

/// <summary>Settings bound from the <c>Accounts</c> configuration section.</summary>
public sealed class AccountOptions
{
    public const string Section = "Accounts";

    /// <summary>The fewest PBKDF2 rounds a new password hash may take: OWASP's figure for PBKDF2-HMAC-SHA256.</summary>
    public const int MinimumPasswordIterations = 600_000;

    /// <summary>
    /// How many PBKDF2 rounds each new password hash takes, at least
    /// <see cref="MinimumPasswordIterations"/>. Raising it applies to every account created
    /// from then on, and to each older one at its next login with the right password; no
    /// stored password stops matching.
    /// </summary>
    public int PasswordIterations { get; set; } = MinimumPasswordIterations;

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

/// <summary>Refuses to start with password hashes weaker than <see cref="AccountOptions.MinimumPasswordIterations"/>.</summary>
/// <remarks>
/// The check on <see cref="AccountOptions.RequireRegistration"/> stays where the server is
/// built, because it depends on the environment the server runs in.
/// </remarks>
public sealed class AccountOptionsValidation : IValidateOptions<AccountOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.PasswordIterations >= AccountOptions.MinimumPasswordIterations
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{AccountOptions.Section}:{nameof(AccountOptions.PasswordIterations)} must be at least " +
                $"{AccountOptions.MinimumPasswordIterations}: fewer rounds make a copy of the accounts quicker to crack");
    }
}
