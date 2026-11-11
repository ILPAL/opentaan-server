using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Rooms;

/// <summary>What a request to create a room must hold, and what its host may change it to.</summary>
/// <remarks>
/// The client's create dialog checks most of this before it sends, so only a modified
/// client is ever refused here: a name that is not empty and not all spaces, a password
/// that is not empty when one is asked for, and a game mode the room's type allows. The
/// dialog always asks for <see cref="GameSlotInfo.SlotCount"/> players, and its password
/// field takes <see cref="MaxPasswordLength"/> characters. The bound on the name's length
/// is the server's own, set by <see cref="RoomOptions.MaxNameLength"/>. The same holds for
/// the host's options dialog; see <see cref="OptionsProblem"/>.
/// </remarks>
public static class RoomRules
{
    /// <summary>The longest password the client's password field takes.</summary>
    public const int MaxPasswordLength = 10;

    /// <summary>The hardest of the three difficulties the client offers a mission room's host. The easiest is 0.</summary>
    public const byte HardestDifficulty = 2;

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

        if (ModesProblem(requested.GameType, requested.GameMode, requested.ChanceMode) is { } modes)
        {
            return modes;
        }

        if (requested.MaxPlayers != GameSlotInfo.SlotCount)
        {
            return $"it asks for {requested.MaxPlayers} players, and every room holds {GameSlotInfo.SlotCount}";
        }

        return null;
    }

    /// <summary>
    /// Why a room playing <paramref name="gameType"/> cannot take the options
    /// <paramref name="requested"/> asks for, for the log, or null when it can.
    /// </summary>
    /// <remarks>
    /// The host's options dialog offers only what is allowed here, so only a modified client
    /// is refused: a game mode the room's type allows and chances the client knows, as when
    /// the room was created; one of <paramref name="maps"/>, which in a room of teams are the
    /// maps the client lists in the room's channel, its random picks included, and in a
    /// mission room the missions it lists; and in a mission room a difficulty no harder than
    /// <see cref="HardestDifficulty"/>. Outside a mission room the difficulty means nothing,
    /// so it is not checked.
    /// </remarks>
    /// <param name="gameType">The kind of match the room plays, which no request changes.</param>
    /// <param name="requested">The options asked for.</param>
    /// <param name="maps">The maps, or in a mission room the missions, a host may pick.</param>
    public static string? OptionsProblem(GameType gameType, PqGameSessionChangeGameOption requested, IReadOnlyCollection<ushort> maps)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(maps);

        if (ModesProblem(gameType, requested.GameMode, requested.ChanceMode) is { } modes)
        {
            return modes;
        }

        if (gameType == GameType.Mission)
        {
            return requested.Difficulty > HardestDifficulty ? $"difficulty {requested.Difficulty} is past the hardest, {HardestDifficulty}"
                : maps.Contains(requested.MapId) ? null
                : $"mission {requested.MapId} is not one the client lists";
        }

        return maps.Contains(requested.MapId)
            ? null
            : $"map {requested.MapId} is not one the client lists in the room's channel";
    }

    // The game modes and chances a room playing gameType can have.
    private static string? ModesProblem(GameType gameType, GameMode gameMode, ChanceMode chanceMode)
    {
        if (!Enum.IsDefined(gameMode))
        {
            return $"game mode {(ushort)gameMode} does not exist";
        }

        // The client's mode picker offers a mission room the first mode alone.
        if (gameType == GameType.Mission && gameMode != GameMode.Items)
        {
            return $"a mission room cannot have game mode {gameMode}";
        }

        if (!Enum.IsDefined(chanceMode))
        {
            return $"chance mode {(byte)chanceMode} does not exist";
        }

        return null;
    }
}
