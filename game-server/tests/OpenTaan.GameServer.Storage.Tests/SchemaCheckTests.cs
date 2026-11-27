using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Storage;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// The check that stops the server on a schema too old for it, read from Flyway's own
/// record of the migrations it ran. Each test edits that record in a database of its own.
/// </summary>
[Collection(nameof(PostgresServer))]
public sealed class SchemaCheckTests(PostgresServer server)
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADatabaseWithEveryMigrationIsReadyForThisCode()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        var source = database.Open();

        Assert.True(await SchemaCheck.AppliedVersionAsync(source, Cancel) >= SchemaCheck.RequiredVersion);
        await Check(source).StartAsync(Cancel);
    }

    [Fact]
    public async Task EveryStoreWorksOnADatabaseAtExactlyTheMigrationTheServerSaysItNeeds()
    {
        // A database upgraded no further than the check lets through must hold every table
        // and column the stores use, or the check would let the server start and then fail.
        await using var database = await server.DatabaseAtAsync(SchemaCheck.RequiredVersion, Cancel);
        var source = database.Open();
        Assert.Equal(SchemaCheck.RequiredVersion, await SchemaCheck.AppliedVersionAsync(source, Cancel));
        await Check(source).StartAsync(Cancel);

        var accounts = new PostgresAccountStore(source, Options.Create(new AccountOptions()));
        var password = await SessionHarness.FastPasswords.HashAsync(new Secret("5f4dcc3b5aa765d61d8327deb882cf99"), Cancel);
        var account = (await accounts.CreateAsync("player@example.test", password, Gender.Female, Cancel))!;
        Assert.NotNull(await accounts.ReplacePasswordAsync(account.AccountId, password, Cancel));

        var characters = new PostgresCharacterStore(source);
        var parts = new AppearanceSlot[AppearanceSlot.Count];
        parts[1] = new AppearanceSlot(110_001, 1, 2, 3);
        var created = await characters.CreateAsync(account.AccountId, "Rồng", id => new Character(
            account.AccountId,
            new CharacterInfo(id, "Rồng", 1, 0, 0, 300, 0, Gender.Female, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty),
            Character.AppearanceOf(Gender.Female, id, 1, parts)), Cancel);
        Assert.Equal(CharacterCreationOutcome.Created, created.Outcome);
        Assert.True(await characters.FinishTutorialAsync(created.Character!.CharacterId, Cancel));
        Assert.True(await characters.IsNameTakenAsync("rỒng", Cancel));
        Assert.Equal(created.Character with { HasFinishedTutorial = true }, Assert.Single(await characters.FindByAccountAsync(account.AccountId, Cancel)));
        var kept = await characters.KeepMatchAsync(
            new MatchRecord(
                Guid.NewGuid(), 1, 11, 0, MissionDifficulty: 0, WinningTeam: 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                [new MatchPayment(created.Character.CharacterId, 0, MatchEnding.Played, Won: true, Experience: 40, Gold: 60)]),
            Cancel);
        Assert.Equal(new CharacterProgress(1, 40, 60, 1, 0), kept[created.Character.CharacterId]);
        Assert.True(await characters.DeleteAsync(account.AccountId, created.Character.CharacterId, Cancel));
    }

    [Fact]
    public async Task ADatabaseOlderThanThisCodeNeedsStopsTheServerAndSaysWhatBothAre()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        await database.ExecuteAsync(
            $"DELETE FROM flyway_schema_history WHERE version::int >= {SchemaCheck.RequiredVersion}", Cancel);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Check(database.Open()).StartAsync(Cancel));

        Assert.Contains($"at migration V{SchemaCheck.RequiredVersion - 1},", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"needs V{SchemaCheck.RequiredVersion}", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADatabaseNewerThanThisCodeIsAccepted()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        var newer = SchemaCheck.RequiredVersion + 1;
        await database.ExecuteAsync(Recorded(newer, success: true), Cancel);
        var source = database.Open();

        Assert.Equal(newer, await SchemaCheck.AppliedVersionAsync(source, Cancel));
        await Check(source).StartAsync(Cancel);
    }

    [Fact]
    public async Task AMigrationThatFailedIsNotCounted()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        await database.ExecuteAsync($"DELETE FROM flyway_schema_history WHERE version::int >= {SchemaCheck.RequiredVersion}", Cancel);
        await database.ExecuteAsync(Recorded(SchemaCheck.RequiredVersion, success: false), Cancel);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Check(database.Open()).StartAsync(Cancel));
    }

    [Fact]
    public async Task ADatabaseFlywayNeverRanOnStopsTheServer()
    {
        await using var database = await server.NewDatabaseAsync(Cancel);
        await database.ExecuteAsync("DROP TABLE flyway_schema_history", Cancel);
        var source = database.Open();

        Assert.Null(await SchemaCheck.AppliedVersionAsync(source, Cancel));
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => Check(source).StartAsync(Cancel));
        Assert.Contains("no schema", refusal.Message, StringComparison.Ordinal);
    }

    private static SchemaCheck Check(Npgsql.NpgsqlDataSource source) => new(source, NullLogger<SchemaCheck>.Instance);

    // A row as Flyway writes one for a migration it ran.
    private static string Recorded(int version, bool success) =>
        "INSERT INTO flyway_schema_history " +
        "(installed_rank, version, description, type, script, installed_by, execution_time, success) " +
        $"SELECT coalesce(max(installed_rank), 0) + 1, '{version}', 'a later change', 'SQL', 'V{version}__a_later_change.sql', current_user, 0, {(success ? "true" : "false")} " +
        "FROM flyway_schema_history";
}
