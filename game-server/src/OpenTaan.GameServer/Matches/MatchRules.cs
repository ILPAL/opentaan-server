using OpenTaan.GameServer.Characters;
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

    /// <summary>
    /// The slot the server's own shot is sent for, which drops its item boxes: no
    /// character's, so no client turns its camera to it, gives anyone ammunition for it, or
    /// sends it back. Every client still reports it played back, naming this slot.
    /// </summary>
    public const byte BoxesShotSlot = 0xFE;

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

    /// <summary>The weapon a character picked as it is played: a random pick becomes the dragon.</summary>
    public static ushort WeaponPlayed(ushort picked) => picked == RandomWeapon ? RandomWeaponPlayedAs : picked;

    /// <summary>
    /// Whether a match played by <paramref name="gameMode"/> has a captain for each team.
    /// Only then does its start name any: the client gives a captain an extra shot with
    /// every shot, in any game mode (see <see cref="PcGameSessionStartGame.CaptainSlots"/>),
    /// so a captain named in another mode, or for one team alone, gives one side an edge.
    /// </summary>
    public static bool HasCaptains(GameMode gameMode) => gameMode is GameMode.CaptainItems or GameMode.CaptainNoItems;

    /// <summary>
    /// Whether a box caught or a bonus in a match played by <paramref name="gameMode"/> gives
    /// an item. In the other modes it fills its team's gauge instead, and its client names
    /// no item.
    /// </summary>
    public static bool GivesItems(GameMode gameMode) => gameMode is GameMode.Items or GameMode.CaptainItems;

    /// <summary>How the character in slot <paramref name="slot"/>, with weapon <paramref name="weaponId"/>, starts.</summary>
    public static GameSlotPlayInfo StartOf(byte slot, ushort weaponId) =>
        new(slot, StartX, StartY, IsAirborne: false, StartingHp, StartingHp, Seed: 0, WeaponPlayed(weaponId), MaxAmmo, Field3C: 0, StartingAmmo: 0);

    /// <summary>
    /// The start of a turn for the character in slot <paramref name="slot"/>, at a mission
    /// level's turn count <paramref name="turnCount"/>: its shot may drop
    /// <paramref name="itemBoxes"/> item boxes, whatever the game mode, since in a mode
    /// without items a box caught fills its catcher's team gauge instead; the items the
    /// clients draw are of the quality <paramref name="options"/> set; and every ghost in
    /// <paramref name="ghostMoves"/> moves on its way.
    /// </summary>
    public static PcGamePlayTurnStart TurnOf(byte slot, ushort turnCount, byte itemBoxes, MatchOptions options, IReadOnlyList<GhostMove> ghostMoves)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(
            slot,
            Field11: 0,
            TurnWind,
            TurnAmmo,
            itemBoxes,
            RemovedItemBoxes: [],
            ghostMoves,
            HpUpdates: [],
            options.ItemDropQuality,
            DropQualityByTeam: [0, 0, 0],
            MissionTurnCount: turnCount);
    }

    /// <summary>
    /// The server's own shot, which drops <paramref name="boxes"/> on the field of a match
    /// between teams. It is sent for <see cref="BoxesShotSlot"/>, and its steps name the
    /// character in <paramref name="slot"/>, whose turn starts next: a client plays a step
    /// only for a slot a character sits in, and a box belongs to whoever catches it.
    /// </summary>
    public static PcGamePlayScenario BoxesShotOf(byte slot, IReadOnlyList<ItemBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        return new(
            Direction: 0,
            BoxesShotSlot,
            new GameScenario(
                slot,
                ActionType: 0,
                [.. boxes.Select(box => new GameScenarioCommandMakeItemBox(slot, GameScenarioCommandMakeItemBox.Type, TimingTick: 0, Duration: 0, box.Id, box.X, box.Y))],
                DelayTimings: [],
                Effects: [],
                WeaponFlags: 0,
                InitialAngle: 0,
                HitSlots: [],
                Seed: 0),
            TotalStats: 0,
            AmmoAddition: 0,
            Integrity: default);
    }

    /// <summary>The end of the turn of the character in slot <paramref name="slot"/>.</summary>
    public static PcGamePlayTurnEnd TurnEndOf(byte slot) => new(slot, TurnSeed, TeamGauge: null);

    /// <summary>The character in slot <paramref name="slot"/> as a ghost of its team, where it was last reported standing, <paramref name="at"/>.</summary>
    public static PcGamePlayMakeGhost GhostOf(byte slot, (int X, int Y) at) => new(slot, at.X, at.Y, Seating.TeamOf(slot));

    /// <summary>Where every character starts, until its client reports where it stands.</summary>
    public static (int X, int Y) StartPosition => (StartX, StartY);

    /// <summary>
    /// The row of a character paid <paramref name="earned"/>, in slot <paramref name="slot"/>,
    /// from where it stood, <paramref name="before"/>: its experience bar from there to where
    /// the experience takes it, a step at the beginning of each level it passes on the way,
    /// and the experience and gold earned, which the result screen shows rising from it.
    /// </summary>
    /// <remarks>
    /// The client counts the experience earned as the last step's less the first's, and a
    /// level gained when the last step's level is above the first's, so there are always two
    /// steps at least. It also puts the last step's level in the room, for everyone in it.
    /// The kills, damage, support and hit rate are not counted yet, and show 0.
    /// </remarks>
    public static GameResultSlot PaidRow(byte slot, CharacterProgress before, Earning earned)
    {
        var after = before.WithExperience(earned.Experience);
        var steps = new List<ExpResult> { StepAt(before.Level, before.Experience) };
        for (var level = (ushort)(before.Level + 1); level < after.Level; level++)
        {
            steps.Add(StepAt(level, LevelTable.BarAt(level)!.Value.Floor));
        }

        steps.Add(StepAt(after.Level, after.Experience));
        return new GameResultSlot(
            slot,
            after.Level,
            (int)earned.Experience,
            steps,
            earned.Gold,
            KillStat: 0,
            ForceStat: 0,
            HitRate: 0,
            SupportStat: 0,
            Bonuses: [],
            ForceMedalAward: 0,
            KillMedalAward: 0,
            SupportMedalAward: 0,
            HasReward: true,
            ExpReward: earned.Experience,
            GoldReward: earned.Gold);
    }

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

    // A step of the experience bar: the level, the experience, and the level's bar.
    private static ExpResult StepAt(ushort level, uint experience)
    {
        var bar = LevelTable.BarAt(level) ?? throw new ArgumentOutOfRangeException(nameof(level), $"There is no level {level}.");
        return new ExpResult(level, experience, bar.Floor, bar.Ceiling);
    }
}
