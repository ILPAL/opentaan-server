using System.Globalization;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Networks;
using Npgsql;
using OpenTaan.Tests.Storage;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresServer))]

namespace OpenTaan.Tests.Storage;

/// <summary>
/// A PostgreSQL 18 server holding a database with every migration applied, shared by the
/// whole run. Each test gets a database of its own, copied from that one.
/// </summary>
/// <remarks>
/// <para>
/// Where Docker runs, as in CI, the server is a container of the image the deployment
/// runs, and the migrations are applied by Flyway, again in the image the deployment runs,
/// so the tests check the schema a deployment gets.
/// </para>
/// <para>
/// Where it does not, <c>OPENTAAN_TEST_POSTGRES</c> can name a server instead, on which the
/// run may create and drop databases. There the migration files are applied in order and
/// Flyway's record of them is written alongside, with what Flyway would write.
/// </para>
/// <para>
/// With neither, the tests skip, except in CI, where they fail: database tests that
/// skipped there would let every check of the database pass without one of them running.
/// </para>
/// <para>
/// The test classes that use it are one collection, <see cref="PostgresTests"/>, and run
/// one after another: the server takes 100 connections, and a race of one class opens 64,
/// which a race of another running alongside could take past the server's limit.
/// </para>
/// </remarks>
public sealed partial class PostgresServer : IAsyncLifetime
{
    private const string PostgresImage = "postgres:18.6";
    private const string FlywayImage = "flyway/flyway:13.7.0";
    private const string ServerVariable = "OPENTAAN_TEST_POSTGRES";

    // In the containers only, which nothing outside this run can reach.
    private const string ContainerUser = "opentaan";
    private const string ContainerPassword = "opentaan";

    private readonly SemaphoreSlim _creating = new(1, 1);
    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private string? _server;
    private string? _template;

    /// <summary>Why there is no server to test against, for a skip message; null when there is one.</summary>
    public string? Absence { get; private set; }

    private static bool InCi => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private static string Migrations => Path.Combine(AppContext.BaseDirectory, "migrations");

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrEmpty(server))
        {
            await MigrateByHandAsync(server);
            return;
        }

        try
        {
            await StartContainersAsync();
        }
        catch (DockerUnavailableException exception) when (!InCi)
        {
            Absence = $"No PostgreSQL to test against: Docker is not running here ({exception.Message}), and {ServerVariable} names no server";
        }
    }

    /// <summary>
    /// A new database with every migration applied, for one test, which the test drops by
    /// disposing it. Skips the test when there is no server.
    /// </summary>
    public async Task<TestDatabase> NewDatabaseAsync(CancellationToken cancellationToken)
    {
        Assert.SkipWhen(Absence is not null, Absence ?? string.Empty);

        var name = $"opentaan_test_{Guid.NewGuid():N}";

        // One copy at a time: a copy cannot be taken of a database another copy is reading.
        await _creating.WaitAsync(cancellationToken);
        try
        {
            await ExecuteAsync(_server!, $"CREATE DATABASE {name} TEMPLATE {_template}", cancellationToken);
        }
        finally
        {
            _creating.Release();
        }

        return new TestDatabase(name, On(_server!, name), drop: ct => ExecuteAsync(_server!, $"DROP DATABASE {name} WITH (FORCE)", ct));
    }

    /// <summary>
    /// A new database with the migrations applied up to <paramref name="version"/> and no
    /// further, as a database not yet upgraded would be. Skips the test when there is no server.
    /// </summary>
    public async Task<TestDatabase> DatabaseAtAsync(int version, CancellationToken cancellationToken)
    {
        Assert.SkipWhen(Absence is not null, Absence ?? string.Empty);

        var name = $"opentaan_test_{Guid.NewGuid():N}";
        await ExecuteAsync(_server!, $"CREATE DATABASE {name}", cancellationToken);
        var database = new TestDatabase(name, On(_server!, name), drop: ct => ExecuteAsync(_server!, $"DROP DATABASE {name} WITH (FORCE)", ct));
        if (_network is not null)
        {
            await MigrateWithFlywayAsync(name, version);
        }
        else
        {
            await MigrateByHandAsync(On(_server!, name), version);
        }

        return database;
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is null && _server is not null && _template is not null)
        {
            await ExecuteAsync(_server, $"DROP DATABASE IF EXISTS {_template} WITH (FORCE)", CancellationToken.None);
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }

        _creating.Dispose();
    }

    private async Task StartContainersAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithDatabase("opentaan")
            .WithUsername(ContainerUser)
            .WithPassword(ContainerPassword)
            .Build();
        await _postgres.StartAsync();
        await MigrateWithFlywayAsync("opentaan", version: null);

        _server = On(_postgres.GetConnectionString(), "postgres");
        _template = "opentaan";
    }

    // Applies the migrations, up to a version or all of them, then exits, as Flyway does
    // before the servers start in compose.
    private async Task MigrateWithFlywayAsync(string database, int? version)
    {
        var builder = new ContainerBuilder(FlywayImage)
            .WithNetwork(_network)
            .WithBindMount(Migrations, "/flyway/sql", AccessMode.ReadOnly)
            .WithEnvironment("FLYWAY_URL", $"jdbc:postgresql://postgres:5432/{database}")
            .WithEnvironment("FLYWAY_USER", ContainerUser)
            .WithEnvironment("FLYWAY_PASSWORD", ContainerPassword)
            .WithEnvironment("FLYWAY_LOCATIONS", "filesystem:/flyway/sql");
        if (version is { } target)
        {
            builder = builder.WithEnvironment("FLYWAY_TARGET", target.ToString(CultureInfo.InvariantCulture));
        }

        await using var flyway = builder.WithCommand("migrate").Build();
        await flyway.StartAsync();
        var exitCode = await flyway.GetExitCodeAsync();
        if (exitCode != 0)
        {
            var (output, errors) = await flyway.GetLogsAsync();
            throw new InvalidOperationException($"Flyway exited with {exitCode}:\n{output}\n{errors}");
        }
    }

    private async Task MigrateByHandAsync(string server)
    {
        _server = On(server, "postgres");
        _template = $"opentaan_template_{Guid.NewGuid():N}";
        await ExecuteAsync(_server, $"CREATE DATABASE {_template}", CancellationToken.None);
        await MigrateByHandAsync(On(server, _template), upTo: null);
    }

    // Applies the migration files in order, up to a version or all of them, and records
    // each as Flyway would.
    private static async Task MigrateByHandAsync(string connectionString, int? upTo)
    {
        await using var database = NpgsqlDataSource.Create(connectionString);
        await ExecuteAsync(database, FlywayHistory, CancellationToken.None);

        var rank = 0;
        foreach (var (version, description, file) in MigrationFiles().Where(migration => upTo is null || migration.Version <= upTo))
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var migration = new NpgsqlCommand(await File.ReadAllTextAsync(file), connection, transaction))
            {
                await migration.ExecuteNonQueryAsync();
            }

            await using (var record = new NpgsqlCommand(
                "INSERT INTO flyway_schema_history " +
                "(installed_rank, version, description, type, script, installed_by, execution_time, success) " +
                "VALUES ($1, $2, $3, 'SQL', $4, current_user, 0, true)",
                connection,
                transaction))
            {
                record.Parameters.Add(new NpgsqlParameter { Value = ++rank });
                record.Parameters.Add(new NpgsqlParameter { Value = version.ToString(CultureInfo.InvariantCulture) });
                record.Parameters.Add(new NpgsqlParameter { Value = description });
                record.Parameters.Add(new NpgsqlParameter { Value = Path.GetFileName(file) });
                await record.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
    }

    // Flyway's record of the migrations it ran, as it creates it on PostgreSQL.
    private const string FlywayHistory = """
        CREATE TABLE flyway_schema_history (
            installed_rank INT           NOT NULL PRIMARY KEY,
            version        VARCHAR(50),
            description    VARCHAR(200)  NOT NULL,
            type           VARCHAR(20)   NOT NULL,
            script         VARCHAR(1000) NOT NULL,
            checksum       INT,
            installed_by   VARCHAR(100)  NOT NULL,
            installed_on   TIMESTAMP     NOT NULL DEFAULT now(),
            execution_time INT           NOT NULL,
            success        BOOLEAN       NOT NULL
        )
        """;

    // Every migration file, in the order Flyway applies them.
    private static IEnumerable<(int Version, string Description, string File)> MigrationFiles() =>
        Directory.EnumerateFiles(Migrations, "V*__*.sql")
            .Select(file => (Match: MigrationName().Match(Path.GetFileName(file)), File: file))
            .Where(migration => migration.Match.Success)
            .Select(migration => (
                int.Parse(migration.Match.Groups[1].Value, CultureInfo.InvariantCulture),
                migration.Match.Groups[2].Value.Replace('_', ' '),
                migration.File))
            .OrderBy(migration => migration.Item1);

    [GeneratedRegex(@"^V(\d+)__(.+)\.sql$")]
    private static partial Regex MigrationName();

    private static string On(string server, string database) =>
        new NpgsqlConnectionStringBuilder(server) { Database = database, Pooling = false }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var database = NpgsqlDataSource.Create(connectionString);
        await ExecuteAsync(database, sql, cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlDataSource database, string sql, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>The test classes that use the one <see cref="PostgresServer"/>, run one after another; see its remarks.</summary>
[CollectionDefinition(nameof(PostgresServer))]
public sealed class PostgresTests;
