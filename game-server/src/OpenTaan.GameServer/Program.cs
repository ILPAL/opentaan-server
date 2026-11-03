using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenTaan.GameServer;
using OpenTaan.GameServer.Accounts;
using OpenTaan.GameServer.Channels;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Lobby;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Characters;
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

builder.Services
    .AddOptions<CharacterOptions>()
    .Bind(builder.Configuration.GetSection(CharacterOptions.Section))
    .Validate(
        o => o.Start.ExperienceCeiling > 0,
        "Characters:Start:ExperienceCeiling must be above zero: the client divides by it to draw the experience bar")
    .Validate(
        o => o.CreatorParts.Count > 0
             && o.CreatorParts.All(part => part is >= 0 and < AppearanceSlot.Count)
             && o.CreatorParts.Distinct().Count() == o.CreatorParts.Count,
        $"Characters:CreatorParts must list distinct appearance parts from 0 to {AppearanceSlot.Count - 1}")
    .ValidateOnStart();

builder.Services
    .AddOptions<ChannelOptions>()
    .Bind(builder.Configuration.GetSection(ChannelOptions.Section))
    .Validate(o => o.List.Count > 0, "Channels:List must name at least one channel")
    .Validate(
        o => o.List.All(channel => channel.Id is not (0 or ushort.MaxValue))
             && o.List.Select(channel => channel.Id).Distinct().Count() == o.List.Count,
        "Channels:List ids must be distinct, and neither 0 nor 65535, which the client uses to mean no channel")
    .Validate(o => o.List.All(channel => channel.Name.Length > 0), "Channels:List names must not be empty")
    .Validate(o => o.List.All(channel => channel.Capacity > 0), "Channels:List capacities must be positive")
    .Validate(
        o => string.IsNullOrEmpty(o.Server.PublicAddress) || AdvertisedAddress.IsIPv4(o.Server.PublicAddress),
        "Channels:Server:PublicAddress must be an IPv4 address written out in full: the client has no room for a name")
    .Validate(o => o.Server.PublicPort is null or (> 0 and <= 65535), "Channels:Server:PublicPort must be between 1 and 65535")
    .ValidateOnStart();

builder.Services
    .AddOptions<PortraitOptions>()
    .Bind(builder.Configuration.GetSection(PortraitOptions.Section))
    .PostConfigure(o => o.Folder = string.IsNullOrEmpty(o.Folder)
        ? null
        : Path.GetFullPath(o.Folder, builder.Environment.ContentRootPath));

builder.Services.AddSingleton<IAccountStore>(provider =>
    new InMemoryAccountStore(provider.GetRequiredService<IOptions<AccountOptions>>().Value.Start.Cash));
builder.Services.AddSingleton<ICharacterStore, InMemoryCharacterStore>();
builder.Services.AddSingleton<AccountHandlers>();
builder.Services.AddSingleton<CharacterHandlers>();
builder.Services.AddSingleton<ChannelHandlers>();
builder.Services.AddSingleton<LobbyHandlers>();
builder.Services.AddSingleton<LobbyRoster>();
builder.Services.AddSingleton<PacketDispatcher>();
builder.Services.AddSingleton<LoggedInAccounts>();

builder.Services.AddSingleton(provider =>
{
    var channels = provider.GetRequiredService<IOptions<ChannelOptions>>().Value;
    var server = provider.GetRequiredService<IOptions<GameServerOptions>>().Value;
    var address = AdvertisedAddress.Choose(
        channels.Server.PublicAddress,
        server.BindAddress,
        (ushort)(channels.Server.PublicPort ?? server.Port),
        AdvertisedAddress.DetectOutbound);

    // Said once, with how it was chosen: a worked-out address is the one most likely to be
    // unreachable from outside, and nothing else would show it.
    provider.GetRequiredService<ILogger<ChannelDirectory>>().LogInformation(
        "Clients are told the channels are at {Address}:{Port} ({Source})", address.Address, address.Port, address.Source);
    return new ChannelDirectory(channels, address);
});

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
    provider.GetRequiredService<ChannelDirectory>(),
    provider.GetRequiredService<LobbyRoster>(),
    provider.GetRequiredService<ILogger<ClientSession>>()));

builder.Services.AddHostedService<GameListener>();

builder.Build().Run();
