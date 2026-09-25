using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Transport;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<GameServerOptions>()
    .Bind(builder.Configuration.GetSection(GameServerOptions.Section))
    .Validate(o => o.Port is > 0 and <= 65535, "GameServer:Port must be between 1 and 65535")
    .Validate(o => IPAddress.TryParse(o.BindAddress, out _), "GameServer:BindAddress must be an IP address")
    .Validate(o => o.MaxConnections > 0 && o.MaxConnectionsPerAddress > 0, "GameServer connection limits must be positive")
    .Validate(o => o.IdleTimeout > TimeSpan.Zero && o.SendTimeout > TimeSpan.Zero, "GameServer timeouts must be positive")
    .Validate(o => o.SendQueueCapacity > 0, "GameServer:SendQueueCapacity must be positive")
    .ValidateOnStart();

builder.Services
    .AddOptions<AccountOptions>()
    .Bind(builder.Configuration.GetSection(AccountOptions.Section))
    .Validate(
        o => o.RequireRegistration || builder.Environment.IsDevelopment(),
        "Accounts:RequireRegistration can be turned off only in the Development environment: " +
        "with it off, anyone can log in to any account")
    .ValidateOnStart();

builder.Services.AddSingleton<IAccountStore, InMemoryAccountStore>();
builder.Services.AddSingleton<AccountHandlers>();
builder.Services.AddSingleton<CharacterHandlers>();
builder.Services.AddSingleton<PacketDispatcher>();
builder.Services.AddSingleton<LoggedInAccounts>();

builder.Services.AddSingleton(provider => PacketNames.Load(
    provider.GetRequiredService<IOptions<GameServerOptions>>().Value.PacketNamesFile,
    builder.Environment.ContentRootPath,
    provider.GetRequiredService<ILogger<PacketNames>>()));

builder.Services.AddSingleton<HandshakeKeySource>(_ =>
{
    // The default generator is safe to share between threads.
    var random = RandomNumberGenerator.Create();
    return () => HandshakeKeys.Generate(random);
});

builder.Services.AddSingleton(provider => new SessionServices(
    provider.GetRequiredService<PacketDispatcher>(),
    provider.GetRequiredService<PacketNames>(),
    provider.GetRequiredService<IOptions<GameServerOptions>>().Value,
    provider.GetRequiredService<HandshakeKeySource>(),
    provider.GetRequiredService<LoggedInAccounts>(),
    provider.GetRequiredService<ILogger<ClientSession>>()));

builder.Services.AddHostedService<GameListener>();

builder.Build().Run();
