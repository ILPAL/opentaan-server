using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Accounts;

/// <summary>
/// Accounts held in memory, gone when the process stops. The store until the database
/// stage, which also brings password hashing at rest and a login throttle.
/// </summary>
/// <remarks>
/// Addresses are compared exactly. Folding case would merge two accounts the client
/// treats as different, and let a guess against one count against the other.
/// </remarks>
/// <param name="startingCash">The cash a new account starts with.</param>
public sealed class InMemoryAccountStore(uint startingCash = 0) : IAccountStore
{
    // The first ids the reference sessions record: account 1001 is player 1.
    private const uint FirstAccountId = 1001;
    private const uint FirstLocalPlayerId = 1;

    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public ValueTask<Account?> FindAsync(string emailAddress, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_accounts.GetValueOrDefault(emailAddress));
        }
    }

    /// <inheritdoc/>
    public ValueTask<Account?> CreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_accounts.ContainsKey(emailAddress) ? null : Add(emailAddress, passwordHash, gender));
        }
    }

    /// <inheritdoc/>
    public ValueTask<Account> FindOrCreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_accounts.GetValueOrDefault(emailAddress) ?? Add(emailAddress, passwordHash, gender));
        }
    }

    private Account Add(string emailAddress, Secret passwordHash, Gender gender)
    {
        var count = (uint)_accounts.Count;
        var account = new Account(FirstAccountId + count, FirstLocalPlayerId + count, emailAddress, passwordHash, gender, startingCash);
        _accounts.Add(emailAddress, account);
        return account;
    }
}
