using OpenTaan.GameServer.Rooms;

namespace OpenTaan.GameServer.Matches;

/// <summary>Which character a match's next turn goes to: the teams take turns, each going through its own characters in slot order.</summary>
/// <remarks>
/// <para>
/// After a character of one team, the turn goes to the other team's next character in play:
/// the first in slot order after the last of that team to have had a turn, or round again
/// from its lowest. So two against two, seated in slots 0 and 1 against 2 and 3, play 0, 2,
/// 1, 3, and no team plays twice in a row while the other has a character left.
/// </para>
/// <para>
/// A team with no character in play is passed over, so a team left alone plays every turn,
/// and a mission, whose players are all on one team, goes through them in slot order. Which
/// team a character is on is where its slot is drawn; see <see cref="Seating.TeamOf"/>.
/// </para>
/// </remarks>
public sealed class TurnOrder
{
    private readonly Dictionary<byte, byte> _lastOfTeam = [];

    /// <summary>Records that the character in <paramref name="slot"/> has the turn.</summary>
    public void Played(byte slot) => _lastOfTeam[Seating.TeamOf(slot)] = slot;

    /// <summary>
    /// The slot of the character whose turn comes after the turn of the one in
    /// <paramref name="slot"/>, among the characters in <paramref name="inPlay"/>; null when
    /// none is in play. The character in <paramref name="slot"/> plays again only when no
    /// other is in play.
    /// </summary>
    public byte? After(byte slot, IReadOnlyCollection<byte> inPlay)
    {
        ArgumentNullException.ThrowIfNull(inPlay);

        var team = Seating.TeamOf(slot);
        return NextOf(OtherTeam(team), inPlay) ?? NextOf(team, inPlay);
    }

    private static byte OtherTeam(byte team) => team == 1 ? (byte)2 : (byte)1;

    // The team's next character in play: the first in slot order after the last of the team
    // to have had a turn, round again from its lowest; its lowest when none of it has had one.
    private byte? NextOf(byte team, IReadOnlyCollection<byte> inPlay)
    {
        var members = inPlay.Where(slot => Seating.TeamOf(slot) == team).Order().ToArray();
        if (members.Length == 0)
        {
            return null;
        }

        return _lastOfTeam.TryGetValue(team, out var last)
            ? members.FirstOrDefault(slot => slot > last, members[0])
            : members[0];
    }
}
