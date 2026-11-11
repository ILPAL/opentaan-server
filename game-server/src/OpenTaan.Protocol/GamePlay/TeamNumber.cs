namespace OpenTaan.Protocol.GamePlay;

/// <summary>A team number as item packets carry it, which the client looks lists up by without checking it.</summary>
internal static class TeamNumber
{
    /// <summary>The value a packet carries for no team.</summary>
    public const byte None = 0xFF;

    /// <summary>Whether <paramref name="team"/> is one of the team numbers the client keeps lists for.</summary>
    public static bool IsTeam(byte team) => team < PcGamePlayTurnStart.TeamCount;

    /// <summary>Whether <paramref name="team"/> is a team number or <see cref="None"/>.</summary>
    public static bool IsTeamOrNone(byte team) => IsTeam(team) || team == None;
}
