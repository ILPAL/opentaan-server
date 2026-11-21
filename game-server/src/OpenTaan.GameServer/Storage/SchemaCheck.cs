using System.Globalization;
using Npgsql;

namespace OpenTaan.GameServer.Storage;

/// <summary>Refuses to start the server on a database whose schema is older than this code needs.</summary>
/// <remarks>
/// <para>
/// The schema belongs to <c>database/migrations</c>, which Flyway applies before the server
/// starts; the server never changes it. So the server checks only that the migration it
/// needs has been applied, in Flyway's own record of what it ran, and stops before it
/// accepts a connection if not, rather than fail on the first query that needs a column.
/// </para>
/// <para>
/// A newer schema is accepted: every migration keeps working with the release before it,
/// so a database can be upgraded before the servers that use it.
/// </para>
/// </remarks>
public sealed partial class SchemaCheck(NpgsqlDataSource database, ILogger<SchemaCheck> logger) : IHostedService
{
    /// <summary>The migration this code needs: V4, the accounts as the game server keeps them.</summary>
    public const int RequiredVersion = 4;

    /// <summary>Checks the schema, and throws, which stops the server, if it is too old.</summary>
    /// <exception cref="InvalidOperationException">The database has no migration at or past <see cref="RequiredVersion"/>.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var applied = await AppliedVersionAsync(database, cancellationToken).ConfigureAwait(false);
        if (applied is not { } version || version < RequiredVersion)
        {
            throw new InvalidOperationException(applied is null
                ? $"The database has no schema: apply database/migrations with Flyway before starting the game server, which needs migration V{RequiredVersion}"
                : $"The database's schema is at migration V{applied}, and this game server needs V{RequiredVersion}: apply database/migrations with Flyway first");
        }

        var target = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        LogReady(logger, target.Database, target.Host, target.Port, version);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The highest migration Flyway has applied to <paramref name="database"/>, or null when it has applied none.</summary>
    public static async Task<int?> AppliedVersionAsync(NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        await using var command = database.CreateCommand(
            "SELECT version FROM flyway_schema_history WHERE success AND version IS NOT NULL");
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            int? highest = null;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Migrations are numbered with whole numbers, V1, V2 and on.
                if (int.TryParse(reader.GetString(0), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                    && version > (highest ?? 0))
                {
                    highest = version;
                }
            }

            return highest;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Accounts are kept in database {Database} on {Host}:{Port}, at migration V{Version}")]
    private static partial void LogReady(ILogger logger, string? database, string? host, int port, int version);
}
