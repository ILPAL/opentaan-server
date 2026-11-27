using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Rooms;

namespace OpenTaan.GameServer.Matches;

/// <summary>What a match pays: the experience and gold.</summary>
/// <param name="Experience">The experience.</param>
/// <param name="Gold">The gold.</param>
public readonly record struct Earning(uint Experience, uint Gold);

/// <summary>Settings bound from the <c>Rewards</c> configuration section: what a match pays each character.</summary>
/// <remarks>
/// <para>
/// The client has no table of rewards: it shows what the server sends, and keeps the
/// experience and gold the server tells it. So the amounts are the server's own. Every
/// character that played a match to its end is paid, its character alive or a ghost; one
/// whose player gave up or left is paid nothing. Each amount is worked out in whole units,
/// any fraction dropped.
/// </para>
/// <para>
/// No match pays more experience than <see cref="LevelTable.FirstLevelCost"/>, the least a
/// level takes, so no match moves a character up more than one level.
/// </para>
/// </remarks>
public sealed class RewardOptions
{
    public const string Section = "Rewards";

    /// <summary>The experience a match between teams pays a character on the losing team, which a win multiplies.</summary>
    public uint TeamMatchExperience { get; set; }

    /// <summary>The gold a match between teams pays a character on the losing team, which a win multiplies.</summary>
    public uint TeamMatchGold { get; set; }

    /// <summary>What a win multiplies a match between teams' experience and gold by.</summary>
    public double WinMultiplier { get; set; }

    /// <summary>
    /// For each mission the client lists, the experience clearing it pays at each
    /// difficulty, easiest first, one for each difficulty a room can pick.
    /// </summary>
    public IReadOnlyDictionary<ushort, IReadOnlyList<uint>> MissionExperience { get; set; } = new Dictionary<ushort, IReadOnlyList<uint>>();

    /// <summary>The share of a clear's experience a lost mission pays, from 0 to 1.</summary>
    public double MissionLostShare { get; set; }

    /// <summary>The gold a mission pays for each unit of the experience it pays.</summary>
    public double MissionGoldPerExperience { get; set; }

    /// <summary>How many times the server tries to keep what a match paid before it gives up and logs the payment as lost.</summary>
    public int PayoutAttempts { get; set; }

    /// <summary>How long the server waits after a failed try before it tries again.</summary>
    public TimeSpan PayoutRetryDelay { get; set; }

    /// <summary>What a match between teams pays a character that played it to the end.</summary>
    /// <param name="won">Whether its team won.</param>
    public Earning BetweenTeams(bool won) => won
        ? Capped(Scaled(TeamMatchExperience, WinMultiplier), Scaled(TeamMatchGold, WinMultiplier))
        : Capped(TeamMatchExperience, TeamMatchGold);

    /// <summary>What mission <paramref name="missionId"/> pays a character that played it to the end.</summary>
    /// <param name="missionId">The mission, one <see cref="MissionExperience"/> lists.</param>
    /// <param name="difficulty">The difficulty it was played at.</param>
    /// <param name="cleared">Whether the players cleared it.</param>
    /// <exception cref="KeyNotFoundException">The mission has no experience listed.</exception>
    public Earning Mission(ushort missionId, byte difficulty, bool cleared)
    {
        var clear = MissionExperience[missionId][difficulty];
        var experience = cleared ? clear : Scaled(clear, MissionLostShare);
        return Capped(experience, Scaled(experience, MissionGoldPerExperience));
    }

    /// <summary>
    /// Why these rewards cannot pay for <paramref name="missions"/>, for the log, or null when
    /// they can: every mission a room may pick needs its experience at each difficulty, and
    /// only those, and every amount must be one a match can pay.
    /// </summary>
    public string? Problem(IEnumerable<ushort> missions)
    {
        ArgumentNullException.ThrowIfNull(missions);

        var listed = missions.ToHashSet();
        var difficulties = RoomRules.HardestDifficulty + 1;
        var missing = listed.Where(mission => !MissionExperience.ContainsKey(mission)).Order().ToArray();
        if (missing.Length > 0)
        {
            return $"{Section}:{nameof(MissionExperience)} gives no experience for mission {string.Join(", ", missing)}, which " +
                   $"{RoomOptions.Section}:{nameof(RoomOptions.Missions)} lists: clearing it could pay nothing";
        }

        var strangers = MissionExperience.Keys.Where(mission => !listed.Contains(mission)).Order().ToArray();
        if (strangers.Length > 0)
        {
            return $"{Section}:{nameof(MissionExperience)} gives experience for mission {string.Join(", ", strangers)}, which " +
                   $"{RoomOptions.Section}:{nameof(RoomOptions.Missions)} does not list";
        }

        var unevenly = MissionExperience.Where(mission => mission.Value.Count != difficulties).Select(mission => mission.Key).Order().ToArray();
        if (unevenly.Length > 0)
        {
            return $"{Section}:{nameof(MissionExperience)} must give mission {string.Join(", ", unevenly)} one amount for each of the " +
                   $"{difficulties} difficulties, easiest first";
        }

        if (!double.IsFinite(WinMultiplier) || WinMultiplier < 0)
        {
            return $"{Section}:{nameof(WinMultiplier)} must be a number from 0 up, and is {WinMultiplier}";
        }

        if (!double.IsFinite(MissionLostShare) || MissionLostShare is < 0 or > 1)
        {
            return $"{Section}:{nameof(MissionLostShare)} must be from 0 to 1, a share of a clear, and is {MissionLostShare}";
        }

        if (!double.IsFinite(MissionGoldPerExperience) || MissionGoldPerExperience < 0)
        {
            return $"{Section}:{nameof(MissionGoldPerExperience)} must be a number from 0 up, and is {MissionGoldPerExperience}";
        }

        if (PayoutAttempts < 1)
        {
            return $"{Section}:{nameof(PayoutAttempts)} must be at least 1, and is {PayoutAttempts}: with none, no match would pay";
        }

        return PayoutRetryDelay < TimeSpan.Zero
            ? $"{Section}:{nameof(PayoutRetryDelay)} cannot be negative, and is {PayoutRetryDelay}"
            : null;
    }

    // An amount times a factor, the fraction dropped, at most the most a field of the client holds.
    private static uint Scaled(uint amount, double factor) => (uint)Math.Min(Math.Floor(amount * factor), uint.MaxValue);

    private static Earning Capped(uint experience, uint gold) => new(Math.Min(experience, LevelTable.FirstLevelCost), gold);
}

/// <summary>Refuses rewards <see cref="RewardOptions.Problem"/> finds wrong for the missions the rooms list, naming what is wrong.</summary>
public sealed class RewardOptionsValidation(IOptions<RoomOptions> rooms) : IValidateOptions<RewardOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RewardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Problem(rooms.Value.Missions.Keys) is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}
