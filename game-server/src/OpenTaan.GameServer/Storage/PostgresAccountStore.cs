using Microsoft.Extensions.Options;
using Npgsql;
using OpenTaan.GameServer.Accounts;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Storage;

/// <summary>Accounts kept in PostgreSQL, the only copy: every request reads them there and waits for every write.</summary>
/// <remarks>
/// The database hands out the ids and decides which of two creations of one address
/// takes it. Addresses are compared exactly, case included, as the client compares them.
/// </remarks>
/// <param name="database">The connections to the database.</param>
/// <param name="options">What a new account starts with.</param>
public sealed class PostgresAccountStore(NpgsqlDataSource database, IOptions<AccountOptions> options) : IAccountStore
{
    private const string Columns = "account_id, player_id, email, hashed_password, gender, cash";

    private const string FindSql = "SELECT " + Columns + " FROM accounts WHERE email = $1";

    // A taken address inserts nothing and returns no row.
    private const string CreateSql =
        "INSERT INTO accounts (email, hashed_password, gender, cash) VALUES ($1, $2, $3, $4) " +
        "ON CONFLICT (email) DO NOTHING RETURNING " + Columns;

    private const string ReplacePasswordSql =
        "UPDATE accounts SET hashed_password = $2 WHERE account_id = $1 RETURNING " + Columns;

    /// <inheritdoc/>
    public async ValueTask<Account?> FindAsync(string emailAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailAddress);

        await using var command = database.CreateCommand(FindSql).With(emailAddress);
        return await SingleOrNoneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Account?> CreateAsync(string emailAddress, StoredPassword password, Gender gender, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emailAddress);
        ArgumentNullException.ThrowIfNull(password);

        await using var command = database.CreateCommand(CreateSql)
            .With(emailAddress)
            .With(password.Format())
            .With((long)gender)
            .With((long)options.Value.Start.Cash);
        return await SingleOrNoneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<Account> FindOrCreateAsync(string emailAddress, StoredPassword password, Gender gender, CancellationToken cancellationToken) =>
        await CreateAsync(emailAddress, password, gender, cancellationToken).ConfigureAwait(false)
        ?? await FindAsync(emailAddress, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("An account was taken by another creation and gone before it could be read");

    /// <inheritdoc/>
    public async ValueTask<Account?> ReplacePasswordAsync(uint accountId, StoredPassword password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        await using var command = database.CreateCommand(ReplacePasswordSql)
            .With((long)accountId)
            .With(password.Format());
        return await SingleOrNoneAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Account?> SingleOrNoneAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    // In the order of Columns.
    private static Account Read(NpgsqlDataReader reader)
    {
        var accountId = reader.Unsigned(0);
        if (!StoredPassword.TryParse(reader.GetString(3), out var password))
        {
            // Never compared as it is: that is how a password kept unhashed would log in.
            throw new InvalidDataException(
                $"Account {accountId}'s password is not in the form this server keeps passwords in, so no login can be checked against it");
        }

        return new Account(
            AccountId: accountId,
            LocalPlayerId: reader.Unsigned(1),
            EmailAddress: reader.GetString(2),
            Password: password,
            Gender: (Gender)reader.GetInt64(4),
            Cash: reader.Unsigned(5));
    }
}
