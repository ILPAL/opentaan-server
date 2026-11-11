using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Matches;

/// <summary>The values a match starts with and every turn starts with, and how a result is built.</summary>
/// <remarks>
/// The starting and turn values are the ones the reference sessions carry, which real
/// clients have played with, in missions and in matches between teams alike: every
/// character starts in the same place with the same health and ammunition, and every turn
/// has the same wind. Picking them per map, per weapon and per turn is for later.
/// </remarks>
public static class MatchRules
{
    /// <summary>The team a mission's players are on: the room's first team.</summary>
    public const byte PlayersTeam = 1;

    /// <summary>The team a mission's own side is on, which wins when every player is out.</summary>
    public const byte MissionsTeam = 2;

    /// <summary>The weapon a player picks to have one chosen for it.</summary>
    public const ushort RandomWeapon = 200;

    /// <summary>The weapon a random pick is played as: the dragon.</summary>
    public const ushort RandomWeaponPlayedAs = 300;

    /// <summary>The health every character starts a match with, which is also the most it can have.</summary>
    public const uint StartingHp = 1000;

    /// <summary>The most ammunition every character's weapon holds.</summary>
    public const ushort MaxAmmo = 100;

    /// <summary>Where every character starts, across the map.</summary>
    public const int StartX = 200;

    /// <summary>Where every character starts, down the map.</summary>
    public const int StartY = 982;

    /// <summary>The wind every turn has.</summary>
    public const uint TurnWind = 20;

    /// <summary>The ammunition every turn's character is given.</summary>
    public const ushort TurnAmmo = 10;

    /// <summary>The value the end of every turn gives its character to keep, as the reference sessions carry.</summary>
    public const uint TurnSeed = 0;

    /// <summary>The seed the start of a match between teams carries: only a mission is laid out from one.</summary>
    public const uint NoMissionSeed = 0;

    /// <summary>The name of a result card that is no character's.</summary>
    public const string EmptyRewardName = "Empty";

    /// <summary>
    /// How long the client gives its player for a turn before it passes for it: a value of
    /// the client's own, which the server neither sends nor can change.
    /// </summary>
    public static TimeSpan ClientTurnLength { get; } = TimeSpan.FromMilliseconds(15_900);

    /// <summary>
    /// How long the client's camera rests on each character in its tour of them before a
    /// match: a value of the client's own. The client reports a match between teams loaded
    /// only once the tour has shown every character.
    /// </summary>
    public static TimeSpan ClientTourStep { get; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the client's tour of the characters takes in a full room: one step for every slot.</summary>
    public static TimeSpan LongestClientTour { get; } = ClientTourStep * GameSlotInfo.SlotCount;

    /// <summary>
    /// The maps the client lists as random picks. It cannot load one: a match on a random
    /// pick is played on a map drawn at its start; see <see cref="RoomOptions.RandomMaps"/>.
    /// </summary>
    public static IReadOnlySet<ushort> RandomMapPicks { get; } = new HashSet<ushort> { 200, 201, 202, 203 };

    /// <summary>The name of a result card's gold.</summary>
    public const string GoldRewardName = "Gold";

    /// <summary>
    /// For each team number, the slot the start of a match names as the team's leader: the
    /// values the reference sessions carry. Leaders matter only in matches between teams.
    /// </summary>
    public static IReadOnlyList<byte> CaptainSlots { get; } = [0, 1, 0];

    /// <summary>The weapon a character picked as it is played: a random pick becomes the dragon.</summary>
    public static ushort WeaponPlayed(ushort picked) => picked == RandomWeapon ? RandomWeaponPlayedAs : picked;

    /// <summary>Whether a match played by <paramref name="gameMode"/> has items in play.</summary>
    public static bool HasItems(GameMode gameMode) => gameMode is GameMode.Items or GameMode.CaptainItems;

    /// <summary>Whether a match played by <paramref name="gameMode"/> has a captain for each team.</summary>
    public static bool HasCaptains(GameMode gameMode) => gameMode is GameMode.CaptainItems or GameMode.CaptainNoItems;

    /// <summary>How the character in slot <paramref name="slot"/>, with weapon <paramref name="weaponId"/>, starts.</summary>
    public static GameSlotPlayInfo StartOf(byte slot, ushort weaponId) =>
        new(slot, StartX, StartY, IsAirborne: false, StartingHp, StartingHp, Seed: 0, WeaponPlayed(weaponId), MaxAmmo, Field3C: 0, StartingAmmo: 0);

    /// <summary>
    /// The start of a turn for the character in slot <paramref name="slot"/>, at a mission
    /// level's turn count <paramref name="turnCount"/>, in a match played by
    /// <paramref name="gameMode"/>: when it has items in play, as many item boxes may drop as
    /// <paramref name="options"/> allow, and none otherwise; the items drawn are of the
    /// quality they set; and every ghost in <paramref name="ghostMoves"/> moves on its way.
    /// </summary>
    public static PcGamePlayTurnStart TurnOf(byte slot, ushort turnCount, GameMode gameMode, MatchOptions options, IReadOnlyList<GhostMove> ghostMoves)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(
            slot,
            Field11: 0,
            TurnWind,
            TurnAmmo,
            HasItems(gameMode) ? options.ItemBoxesPerTurn : (byte)0,
            RemovedItemBoxes: [],
            ghostMoves,
            HpUpdates: [],
            options.ItemDropQuality,
            DropQualityByTeam: [0, 0, 0],
            MissionTurnCount: turnCount);
    }

    /// <summary>The end of the turn of the character in slot <paramref name="slot"/>.</summary>
    public static PcGamePlayTurnEnd TurnEndOf(byte slot) => new(slot, TurnSeed, TeamGauge: null);

    /// <summary>The character in slot <paramref name="slot"/> as a ghost of its team, where it was last reported standing, <paramref name="at"/>.</summary>
    public static PcGamePlayMakeGhost GhostOf(byte slot, (int X, int Y) at) => new(slot, at.X, at.Y, Seating.TeamOf(slot));

    /// <summary>Where every character starts, until its client reports where it stands.</summary>
    public static (int X, int Y) StartPosition => (StartX, StartY);

    /// <summary>
    /// The result of a match <paramref name="winningTeam"/> won, with a row for each slot:
    /// for a slot holding a character, its level, and nothing earned.
    /// </summary>
    /// <param name="winningTeam">The team that won.</param>
    /// <param name="levels">The level of the character in each slot, null for a slot with none, by slot.</param>
    public static GameResult ResultOf(byte winningTeam, IReadOnlyList<ushort?> levels) => new(
        winningTeam,
        [.. levels.Select((level, slot) => level is { } held ? GameResultSlot.NothingEarned((byte)slot, held) : GameResultSlot.Empty((byte)slot))],
        WinnerPointDelta: 0,
        LoserPointDelta: 0);

    /// <summary>
    /// The result screen's cards: for each slot holding a character, a card of no gold
    /// naming it, since nothing is granted yet, and for every other slot a card no
    /// character's.
    /// </summary>
    /// <param name="characters">The character in each slot, null for a slot with none, by slot.</param>
    public static PcResultSetReward RewardsOf(IReadOnlyList<uint?> characters) => new(
    [
        .. characters.Select(character => character is { } id
            ? new GameRewardGold(GoldRewardName, id, Amount: 0, HasPcRoomBonusCard: false, PcRoomBonusGold: 0)
            : new GameRewardGold(EmptyRewardName, CharacterId: 0, Amount: 0, HasPcRoomBonusCard: false, PcRoomBonusGold: 0)),
    ]);
}
