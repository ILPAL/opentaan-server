using OpenTaan.GameServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<GameServerOptions>()
    .Bind(builder.Configuration.GetSection(GameServerOptions.Section))
    .Validate(o => o.Port is > 0 and <= 65535, "GameServer:Port must be between 1 and 65535")
    .Validate(o => System.Net.IPAddress.TryParse(o.BindAddress, out _), "GameServer:BindAddress must be an IP address")
    .ValidateOnStart();

builder.Services.AddHostedService<GameListener>();

builder.Build().Run();
