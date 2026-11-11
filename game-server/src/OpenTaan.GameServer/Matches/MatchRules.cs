using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.Results;

namespace OpenTaan.GameServer.Matches;

/// <summary>The values a match starts with and every turn starts with, and how a result is built.</summary>
/// <remarks>
/// The starting and turn values are the ones the reference sessions carry, which real
/// clients have played with: every character starts in the same place with the same
/// health and ammunition, and every turn has the same wind. Picking them per map, per
/// weapon and per turn belongs to matches between teams.
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

    /// <summary>How good the items every turn drops are.</summary>
    public const uint TurnDropQuality = 20;

    /// <summary>The name of a result card that is no character's.</summary>
    public const string EmptyRewardName = "Empty";

    /// <summary>The name of a result card's gold.</summary>
    public const string GoldRewardName = "Gold";

    /// <summary>
    /// For each team number, the slot the start of a match names as the team's leader: the
    /// values the reference sessions carry. Leaders matter only in matches between teams.
    /// </summary>
    public static IReadOnlyList<byte> CaptainSlots { get; } = [0, 1, 0];

    /// <summary>The weapon a character picked as it is played: a random pick becomes the dragon.</summary>
    public static ushort WeaponPlayed(ushort picked) => picked == RandomWeapon ? RandomWeaponPlayedAs : picked;

    /// <summary>How the character in slot <paramref name="slot"/>, with weapon <paramref name="weaponId"/>, starts.</summary>
    public static GameSlotPlayInfo StartOf(byte slot, ushort weaponId) =>
        new(slot, StartX, StartY, IsAirborne: false, StartingHp, StartingHp, Seed: 0, WeaponPlayed(weaponId), MaxAmmo, Field3C: 0, StartingAmmo: 0);

    /// <summary>The start of a turn for the character in slot <paramref name="slot"/>, in a mission's round <paramref name="round"/>.</summary>
    public static PcGamePlayTurnStart TurnOf(byte slot, ushort round) => new(
        slot,
        Field11: 0,
        TurnWind,
        TurnAmmo,
        ItemBoxBudget: 0,
        RemovedItemBoxes: [],
        GhostMoves: [],
        HpUpdates: [],
        TurnDropQuality,
        DropQualityByTeam: [0, 0, 0],
        round);

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
