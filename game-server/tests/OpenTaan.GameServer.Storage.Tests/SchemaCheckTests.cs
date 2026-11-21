using Microsoft.Extensions.Logging.Abstractions;
using OpenTaan.GameServer.Storage;

namespace OpenTaan.Tests.Storage;

/// <summary>
/// The check that stops the server on a schema too old for it, read from Flyway's own
/// record of the migrations it ran. Each test edits that record in a database of its own.
/// </summary>
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
