using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Characters;

namespace OpenTaan.GameServer.Storage;

/// <summary>Registers the stores the server keeps in PostgreSQL.</summary>
public static class StorageServices
{
    /// <summary>
    /// Keeps accounts and characters in the PostgreSQL database <c>ConnectionStrings:Game</c>
    /// names, and adds the check that stops the server on a schema too old for it. Call it
    /// before the listener is added, so the check runs first.
    /// </summary>
    public static IServiceCollection AddPostgresStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => GameDatabase.Open(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IAccountStore, PostgresAccountStore>();
        services.AddSingleton<ICharacterStore, PostgresCharacterStore>();
        services.AddHostedService<SchemaCheck>();
        return services;
    }
}
