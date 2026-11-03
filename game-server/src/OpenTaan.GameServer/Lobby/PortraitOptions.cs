namespace OpenTaan.GameServer.Lobby;

/// <summary>Settings bound from the <c>Portraits</c> configuration section.</summary>
public sealed class PortraitOptions
{
    public const string Section = "Portraits";

    /// <summary>
    /// The folder the pictures clients send of their characters are kept in, one file per
    /// character named by its id, each replacing the last. A relative path is taken from
    /// the content root. When empty, the pictures are checked and dropped.
    /// </summary>
    public string? Folder { get; set; }
}
