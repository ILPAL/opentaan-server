using OpenTaan.GameServer.Accounts;

namespace OpenTaan.GameServer.Sessions;

/// <summary>Which connection each logged-in account is live on. Shared by every connection.</summary>
/// <remarks>
/// <para>
/// An account is live on one connection at a time. Two copies of one account would each
/// act on the same account's state, and the client stops neither a second client nor
/// one account named in both of its slots.
/// </para>
/// <para>
/// Checking that an account is free and taking it are one step under one lock, so of two
/// logins for one account arriving at the same moment exactly one succeeds.
/// </para>
/// </remarks>
public sealed class LoggedInAccounts
{
    private readonly Dictionary<uint, ClientSession> _holders = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Takes every account in <paramref name="accounts"/> for <paramref name="session"/>,
    /// or none of them.
    /// </summary>
    /// <returns>Null when they were taken; otherwise the connection one of them is live on.</returns>
    public ClientSession? TryClaim(ClientSession session, IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(accounts);

        lock (_lock)
        {
            foreach (var account in accounts)
            {
                if (_holders.TryGetValue(account.AccountId, out var holder) && holder != session)
                {
                    return holder;
                }
            }

            foreach (var account in accounts)
            {
                _holders[account.AccountId] = session;
            }

            return null;
        }
    }

    /// <summary>Frees the accounts <paramref name="session"/> holds, leaving any another connection holds.</summary>
    public void Release(ClientSession session, IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(accounts);

        lock (_lock)
        {
            foreach (var account in accounts)
            {
                if (_holders.TryGetValue(account.AccountId, out var holder) && holder == session)
                {
                    _holders.Remove(account.AccountId);
                }
            }
        }
    }

    /// <summary>The connection <paramref name="accountId"/> is live on, or null.</summary>
    public ClientSession? HolderOf(uint accountId)
    {
        lock (_lock)
        {
            return _holders.GetValueOrDefault(accountId);
        }
    }
}
