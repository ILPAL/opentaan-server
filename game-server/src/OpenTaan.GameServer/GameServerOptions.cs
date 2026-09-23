namespace OpenTaan.GameServer;

/// <summary>Settings bound from the <c>GameServer</c> configuration section.</summary>
public sealed class GameServerOptions
{
    public const string Section = "GameServer";

    /// <summary>Address the TCP listener binds to.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Port game clients connect to.</summary>
    public int Port { get; set; } = 2180;
}
