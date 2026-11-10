using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>What a request to create a room must hold.</summary>
/// <remarks>
/// The client's create dialog checks most of this before it sends, so only a modified
/// client is ever refused here: a name that is not empty and not all spaces, a password
/// that is not empty when one is asked for, and a game mode the room's type allows. The
/// dialog always asks for <see cref="GameSlotInfo.SlotCount"/> players, and its password
/// field takes <see cref="MaxPasswordLength"/> characters. The bound on the name's length
/// is the server's own, set by <see cref="RoomOptions.MaxNameLength"/>.
/// </remarks>
public static class RoomRules
{
    /// <summary>The longest password the client's password field takes.</summary>
    public const int MaxPasswordLength = 10;

    /// <summary>Why <paramref name="requested"/> cannot be created, for the log, or null when it can.</summary>
    public static string? Problem(GameSessionInfo requested, RoomOptions options)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(requested.Name))
        {
            return "its name is empty or all spaces";
        }

        if (requested.Name.Length > options.MaxNameLength)
        {
            return $"its name is {requested.Name.Length} long, past the {options.MaxNameLength} allowed";
        }

        if (requested.Password is { Length: 0 })
        {
            return "it asks for a password and gives none";
        }

        if (requested.Password is { Length: > MaxPasswordLength })
        {
            return $"its password is {requested.Password.Length} long, past the client's {MaxPasswordLength}";
        }

        if (!Enum.IsDefined(requested.GameType))
        {
            return $"game type {(byte)requested.GameType} does not exist";
        }

        if (!Enum.IsDefined(requested.GameMode))
        {
            return $"game mode {(ushort)requested.GameMode} does not exist";
        }

        // The client's mode picker offers a mission room the first mode alone.
        if (requested.GameType == GameType.Mission && requested.GameMode != GameMode.Items)
        {
            return $"a mission room cannot have game mode {requested.GameMode}";
        }

        if (!Enum.IsDefined(requested.ChanceMode))
        {
            return $"chance mode {(byte)requested.ChanceMode} does not exist";
        }

        if (requested.MaxPlayers != GameSlotInfo.SlotCount)
        {
            return $"it asks for {requested.MaxPlayers} players, and every room holds {GameSlotInfo.SlotCount}";
        }

        return null;
    }
}
