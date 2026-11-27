using Microsoft.Extensions.Options;
using Npgsql;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Storage;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.Storage;

/// <summary>The account contract, against accounts kept in PostgreSQL, and what only the database can show.</summary>
[Collection(nameof(PostgresServer))]
public sealed class PostgresAccountStoreTests(PostgresServer server) : AccountStoreContract, IAsyncDisposable
{
    private TestDatabase? _database;

    [Fact]
    public async Task APasswordNotInTheFormThisServerKeepsIsNeverComparedAndSaysWhichAccount()
    {
        var store = await NewStoreAsync();
        await store.CreateAsync("player@example.test", await PasswordAsync(), Gender.Male, Cancel);

        // As an account would look had its password been kept as the client sent it.
        await _database!.ExecuteAsync($"UPDATE accounts SET hashed_password = '{Digest.Reveal()}'", Cancel);

        var problem = await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindAsync("player@example.test", Cancel));
        Assert.Contains("Account 1001", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAccountWithNoGender()
    {
        var store = await NewStoreAsync();

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            async () => await store.CreateAsync("player@example.test", await PasswordAsync(), Gender.Unspecified, Cancel));

        Assert.Equal("gender_is_male_or_female", refusal.ConstraintName);
    }

    [Fact]
    public async Task TheDatabaseRefusesAnAccountWithNoPassword()
    {
        await NewStoreAsync();

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => _database!.ExecuteAsync("INSERT INTO accounts (email, gender) VALUES ('player@example.test', 1)", Cancel));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, refusal.SqlState);
    }

    protected override async Task<IAccountStore> NewStoreAsync()
    {
        _database = await server.NewDatabaseAsync(Cancel);
        return Store();
    }

    // A store over new connections, as after the server restarts.
    protected override Task<IAccountStore> ReopenAsync(IAccountStore store) => Task.FromResult<IAccountStore>(Store());

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private PostgresAccountStore Store() =>
        new(_database!.Open(), Options.Create(new AccountOptions { Start = new AccountStart { Cash = StartingCash } }));
}
