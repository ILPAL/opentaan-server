using Npgsql;

namespace OpenTaan.Tests.Storage;

/// <summary>A database of one test's own, with every migration applied, dropped when disposed.</summary>
/// <param name="name">The database's name.</param>
/// <param name="connectionString">How to reach it.</param>
/// <param name="drop">Drops it.</param>
public sealed class TestDatabase(string name, string connectionString, Func<CancellationToken, Task> drop) : IAsyncDisposable
{
    private readonly List<NpgsqlDataSource> _opened = [];

    /// <summary>The database's name.</summary>
    public string Name { get; } = name;

    /// <summary>
    /// A new set of connections to the database, as a server starting up would open, closed
    /// when the database is dropped. What one set wrote, the next reads only from the database.
    /// </summary>
    public NpgsqlDataSource Open()
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = true };
        var opened = NpgsqlDataSource.Create(builder.ConnectionString);
        lock (_opened)
        {
            _opened.Add(opened);
        }

        return opened;
    }

    /// <summary>Runs SQL against the database, as a test arranging what it checks.</summary>
    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var database = NpgsqlDataSource.Create(connectionString);
        await using var command = database.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlDataSource[] opened;
        lock (_opened)
        {
            opened = [.. _opened];
        }

        foreach (var source in opened)
        {
            await source.DisposeAsync();
        }

        await drop(CancellationToken.None);
    }
}
