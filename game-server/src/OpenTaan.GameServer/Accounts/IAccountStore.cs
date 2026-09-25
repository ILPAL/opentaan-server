using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Accounts;

/// <summary>Where accounts are kept. Shared by every connection.</summary>
/// <remarks>
/// Shared rather than held per connection because the client checks or creates an
/// account on one connection, closes it, and logs in on a new one.
/// </remarks>
public interface IAccountStore
{
    /// <summary>The account known by <paramref name="emailAddress"/>, or null.</summary>
    ValueTask<Account?> FindAsync(string emailAddress, CancellationToken cancellationToken);

    /// <summary>Creates an account, or returns null when the address is taken.</summary>
    ValueTask<Account?> CreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken);

    /// <summary>The account known by <paramref name="emailAddress"/>, created with these values if there is none.</summary>
    ValueTask<Account> FindOrCreateAsync(string emailAddress, Secret passwordHash, Gender gender, CancellationToken cancellationToken);
}
