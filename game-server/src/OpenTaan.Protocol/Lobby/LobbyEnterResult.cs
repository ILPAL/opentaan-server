namespace OpenTaan.Protocol.Lobby;

/// <summary>The answer to <see cref="PqLobbyEnter"/>.</summary>
/// <remarks>
/// Any refusal sends the client back to its channel-selection screen with the refusal's
/// message. Every refusal but <see cref="Failed"/> is about arena teams.
/// </remarks>
public enum LobbyEnterResult : byte
{
    /// <summary>The player is in the lobby; the client shows it.</summary>
    Entered = 0,

    /// <summary>The arena team does not exist.</summary>
    TeamNotFound = 1,

    /// <summary>The arena team is out of step with the server.</summary>
    TeamOutOfSync = 2,

    /// <summary>The arena team is full.</summary>
    TeamFull = 3,

    /// <summary>Refused for any other reason; the client reports an unknown error.</summary>
    Failed = 4,
}
