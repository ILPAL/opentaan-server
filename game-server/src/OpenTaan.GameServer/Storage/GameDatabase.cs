using Npgsql;

namespace OpenTaan.GameServer.Storage;

/// <summary>Opens the PostgreSQL database the game server keeps its accounts and characters in.</summary>
public static class GameDatabase
{
    /// <summary>The name of the connection string, read from <c>ConnectionStrings:Game</c>.</summary>
    public const string ConnectionStringName = "Game";

    /// <summary>
    /// The setting the database password is read from when the connection string has none:
    /// the file <c>db_password</c> among a container's secrets, or a user secret on a
    /// development machine. It is never written in a settings file.
    /// </summary>
    public const string PasswordKey = "db_password";

    /// <summary>The connections to the database <paramref name="configuration"/> names.</summary>
    /// <exception cref="InvalidOperationException">No database is named.</exception>
    public static NpgsqlDataSource Open(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var named = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(named))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} must name the PostgreSQL database the game server keeps its accounts and characters in, " +
                "such as Host=127.0.0.1;Port=5432;Database=opentaan;Username=opentaan");
        }

        var connection = new NpgsqlConnectionStringBuilder(named);
        if (string.IsNullOrEmpty(connection.Password) && configuration[PasswordKey] is { Length: > 0 } password)
        {
            // A secret written with echo ends in a newline that is no part of the password.
            connection.Password = password.TrimEnd('\r', '\n');
        }

        if (string.IsNullOrEmpty(connection.ApplicationName))
        {
            connection.ApplicationName = "opentaan-game-server";
        }

        return new NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
    }
}
