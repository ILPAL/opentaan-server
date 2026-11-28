using OpenTaan.GameServer.Rooms;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.GameServer.Matches;

/// <summary>The recorded statistics for one character in a match.</summary>
/// <param name="Kills">Enemy kills credited to the character.</param>
/// <param name="Damage">Damage the character dealt to enemies.</param>
/// <param name="Support">Points from assists and item gifts.</param>
/// <param name="HitRate">The fraction of the character's accepted shots that damaged an enemy.</param>
public readonly record struct MatchStatistics(byte Kills, uint Damage, uint Support, float HitRate)
{
    /// <summary>Enemy kills the character helped a teammate make.</summary>
    public uint Assists { get; init; }
}

/// <summary>
/// Records one match's statistics under the room lock from accepted shots, death reports
/// and item gifts. The shots are the clients' work, including the damage they report.
/// </summary>
internal sealed class MatchStats
{
    private const byte FirstEnemySlot = 100;
    private const byte ItemSlots = 3;
    private const uint AssistPoints = 100;
    private const uint ItemGiftPoints = 50;

    private readonly Totals[] _slots = new Totals[GameSlotInfo.SlotCount];
    private readonly Dictionary<byte, HashSet<byte>> _contributors = [];
    private readonly HashSet<byte> _shotDeaths = [];
    private readonly HashSet<byte> _missionDeaths = [];
    private readonly HashSet<(byte Giver, byte ItemSlot)> _gifts = [];

    public MatchStatistics Of(byte slot)
    {
        var totals = _slots[slot];
        var hitRate = totals.ShotsFired == 0 ? 0 : (float)((double)totals.ShotsHit / totals.ShotsFired);
        return new MatchStatistics(totals.Kills, totals.Damage, totals.Support, hitRate) { Assists = totals.Assists };
    }

    public void NewTurn()
    {
        _shotDeaths.Clear();
        _gifts.Clear();
    }

    public void NewMissionLevel()
    {
        // An enemy slot can name a new enemy in the next level.
        _missionDeaths.Clear();
        _contributors.Clear();
    }

    public void RecordShot(Match match, PcGamePlayScenario shot)
    {
        var shooter = shot.FiringSlot;
        var team = Seating.TeamOf(shooter);
        var hit = false;
        foreach (var command in shot.Scenario.Commands)
        {
            var target = command.SlotIndex;
            if (!IsEnemyInPlay(match, team, target))
            {
                continue;
            }

            // The client subtracts the reported amount as a signed health delta.
            if (command is GameScenarioCommandDamage
                { CommandType: GameScenarioCommandDamage.Type, DamageAmount: > 0 and <= int.MaxValue } damage)
            {
                Damaged(shooter, target, damage.DamageAmount);
                hit = true;
            }
            else if (match.IsMission && command.CommandType is GameScenarioCommand.DeathType or GameScenarioCommand.EnemyDeathType)
            {
                _missionDeaths.Add(target);
                Killed(match, shooter, target);
            }
            else if (!match.IsMission && command.CommandType == GameScenarioCommand.DeathType)
            {
                _shotDeaths.Add(target);
            }
        }

        ref var totals = ref _slots[shooter];
        totals.ShotsFired++;
        if (hit)
        {
            totals.ShotsHit++;
        }
    }

    public void RecordPlayerDeath(Match match, byte slot, bool fromShot)
    {
        if (fromShot && match.Phase == MatchPhase.Shot && match.TurnSlot is { } shooter
            && match.InPlay(slot) && _shotDeaths.Contains(slot))
        {
            Killed(match, shooter, slot);
        }
        else
        {
            _contributors.Remove(slot);
        }
    }

    public void RecordGift(Match match, PcGamePlayUseItem use)
    {
        var giver = use.SlotIndex;
        var team = Seating.TeamOf(giver);
        if (match.Phase != MatchPhase.Turn || match.TurnSlot is not { } receiver || receiver == giver
            || !match.InPlay(receiver) || Seating.TeamOf(receiver) != team || use.Team != team
            || use.ItemSlot >= ItemSlots || use.ItemId is ItemDrops.NoItem or ushort.MaxValue
            || match.EndingOf(giver) != MatchEnding.Played)
        {
            return;
        }

        var ghostGift = use.GhostGift != 0 && use.CaptainGift == 0 && match.IsDead(giver);
        var captainGift = use.CaptainGift != 0 && use.GhostGift == 0 && match.InPlay(giver)
            && match.CaptainSlots[team] == receiver;
        // A gift uses an item from the giver's bar. Score one report per slot per turn.
        if ((ghostGift || captainGift) && _gifts.Add((giver, use.ItemSlot)))
        {
            AddSupport(giver, ItemGiftPoints);
        }
    }

    private bool IsEnemyInPlay(Match match, byte shooterTeam, byte target) => match.IsMission
        ? target >= FirstEnemySlot && !_missionDeaths.Contains(target)
        : match.InPlay(target) && Seating.TeamOf(target) != shooterTeam;

    private void Damaged(byte shooter, byte target, uint amount)
    {
        ref var damage = ref _slots[shooter].Damage;
        damage = DisplayValue(damage, amount);
        if (!_contributors.TryGetValue(target, out var contributors))
        {
            contributors = [];
            _contributors.Add(target, contributors);
        }

        contributors.Add(shooter);
    }

    private void Killed(Match match, byte shooter, byte target)
    {
        ref var kills = ref _slots[shooter].Kills;
        kills = (byte)Math.Min(kills + 1, byte.MaxValue);
        if (_contributors.Remove(target, out var contributors))
        {
            var team = Seating.TeamOf(shooter);
            foreach (var assistant in contributors)
            {
                if (assistant != shooter && Seating.TeamOf(assistant) == team
                    && match.EndingOf(assistant) == MatchEnding.Played)
                {
                    _slots[assistant].Assists++;
                    AddSupport(assistant, AssistPoints);
                }
            }
        }
    }

    private void AddSupport(byte slot, uint points)
    {
        ref var support = ref _slots[slot].Support;
        support = DisplayValue(support, points);
    }

    // The client prints force and support as signed integers.
    private static uint DisplayValue(uint current, uint added) => (uint)Math.Min((ulong)current + added, int.MaxValue);

    private struct Totals
    {
        public byte Kills;
        public uint Damage;
        public uint Assists;
        public uint Support;
        public ulong ShotsFired;
        public ulong ShotsHit;
    }
}
