using OpenTaan.GameServer.Matches;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.GamePlay;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A match's items and ghosts: passed on to the others, and, between teams, the items decided on the server.</summary>
/// <remarks>
/// <para>
/// In a mission, the clients decide every item: the shooter's client places the item boxes
/// its shot drops, as many as <see cref="MatchOptions.ItemBoxesPerTurn"/> allows, and the
/// client that catches a box, or is given a bonus at the end of a turn, draws the item; in a
/// mode without items, it fills its team's gauge instead, and the catch or the bonus carries
/// the gauge's change. The server passes on what each client tells of its own characters,
/// unchanged, once it has checked that the packet names one of the sender's characters in a
/// match still running.
/// </para>
/// <para>
/// Between teams, the server keeps the boxes on the field, places boxes of its own at the
/// start of every turn, and draws the item of every box a ghost catches and of every bonus,
/// from <see cref="ItemDrops"/>: a client gives either only from the server's copy, so
/// every screen gives the item the server drew. A ghost's catch counts only for a box on the
/// field, and a bonus only for the turn's shooter, once a shot: anything else is dropped,
/// and no screen gives it. A box a missile catches is drawn by the shooter's client, which
/// has played its shot before the server sees it; see <see cref="Shoot"/>.
/// </para>
/// <para>
/// Who is told depends on the packet. A client acts on the server's copy of an item used,
/// spent or thrown away, of a box caught and of a bonus, and gives no item of its own
/// accord, so those go to everyone, the sender included; so does a ghost's target. Where an
/// item went goes back to its sender alone, and where a ghost stopped to everyone else.
/// </para>
/// </remarks>
public sealed partial class RoomDirectory
{
    /// <summary>Passes an item used to everyone in the match and records support for a gift.</summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? UseItem(ClientSession session, PcGamePlayUseItem use)
    {
        ArgumentNullException.ThrowIfNull(use);
        return PassOn(session, use.SlotIndex, use, toSenderOnly: false);
    }

    /// <summary>Passes the items a shot spent to everyone in the match, the sender included.</summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? SpendItem(ClientSession session, PcGamePlaySpendItem spend)
    {
        ArgumentNullException.ThrowIfNull(spend);
        return PassOn(session, spend.SlotIndex, spend, toSenderOnly: false);
    }

    /// <summary>Passes an item thrown away to everyone in the match, the sender included.</summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? ForsakeItem(ClientSession session, PcGamePlayForsakeItem forsake)
    {
        ArgumentNullException.ThrowIfNull(forsake);
        return PassOn(session, forsake.SlotIndex, forsake, toSenderOnly: false);
    }

    /// <summary>Passes an item in use taken off to everyone in the match, the sender included.</summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? ForsakeActiveItem(ClientSession session, PcGamePlayForsakeActiveItem forsake)
    {
        ArgumentNullException.ThrowIfNull(forsake);
        return PassOn(session, forsake.SlotIndex, forsake, toSenderOnly: false);
    }

    /// <summary>
    /// Passes a box a ghost caught to everyone in the match, the sender included, whose clients
    /// give the item then. Between teams, only a ghost's catch of a box on the field counts,
    /// which takes it off the field, and in a mode with items the server draws the item.
    /// </summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? EatItemBox(ClientSession session, PcGamePlayEatItemBox caught)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(caught);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is { Match.IsMission: true })
            {
                return PassOnLocked(session, caught.SlotIndex, caught, toSenderOnly: false);
            }

            if (GhostRoomLocked(session, caught.SlotIndex, out var why) is not { } room)
            {
                return why;
            }

            if (!room.Match!.Field.Caught(caught.Catch.ItemBoxId))
            {
                return $"item box {caught.Catch.ItemBoxId:X16} is not on the field";
            }

            SendToRoomLocked(room, new PcGamePlayEatItemBox(caught.SlotIndex, DrawnLocked(room, caught.Catch), caught.TeamGauge));
            return null;
        }
    }

    /// <summary>
    /// Passes a bonus item to everyone in the match, the sender included, whose clients give
    /// the item then. Between teams, only the bonus owed to a shooter for its shot counts, and
    /// in a mode with items the server draws the item.
    /// </summary>
    /// <returns>Null once passed on; otherwise why it was dropped, for the log.</returns>
    public string? EatBonusItem(ClientSession session, PcGamePlayEatBonusItem bonus)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bonus);

        lock (_lock)
        {
            if (PlayingRoomLocked(session) is not { } room)
            {
                return NotInMatch;
            }

            var match = room.Match!;
            if (match.IsMission)
            {
                return PassOnLocked(session, bonus.SlotIndex, bonus, toSenderOnly: false);
            }

            if (!match.IsPlayedBy(bonus.SlotIndex, session))
            {
                return $"slot {bonus.SlotIndex} is not one of its characters";
            }

            if (!match.TakeBonus(bonus.SlotIndex))
            {
                return $"no bonus is owed to slot {bonus.SlotIndex}";
            }

            SendToRoomLocked(room, new PcGamePlayEatBonusItem(bonus.SlotIndex, DrawnLocked(room, bonus.Catch), bonus.TeamGauge));
            return null;
        }
    }

    /// <summary>Sends where an item went back to its sender alone, as the reference sessions do.</summary>
    /// <returns>Null once sent back; otherwise why it was dropped, for the log.</returns>
    public string? NotifyEatItem(ClientSession session, PcGamePlayNotifyEatItem notify)
    {
        ArgumentNullException.ThrowIfNull(notify);
        return PassOn(session, notify.SlotIndex, notify, toSenderOnly: true);
    }

    /// <summary>
    /// Records where a dead character's ghost is to go, and passes it to everyone in the match,
    /// the sender included; every start of a turn carries the ghost's move until it is there.
    /// </summary>
    /// <returns>Null once recorded; otherwise why it was dropped, for the log.</returns>
    public string? AimGhost(ClientSession session, PcGamePlaySetGhostTarget aim)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aim);

        lock (_lock)
        {
            if (GhostRoomLocked(session, aim.SlotIndex, out var why) is not { } room)
            {
                return why;
            }

            room.Match!.AimGhost(aim.SlotIndex, aim.X, aim.Y);
            SendToRoomLocked(room, aim);
            return null;
        }
    }

    /// <summary>Records where a ghost stopped, and passes it to everyone else in the match.</summary>
    /// <returns>Null once recorded; otherwise why it was dropped, for the log.</returns>
    public string? PlaceGhost(ClientSession session, PcGamePlaySetGhostPos place)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(place);

        lock (_lock)
        {
            if (GhostRoomLocked(session, place.SlotIndex, out var why) is not { } room)
            {
                return why;
            }

            room.Match!.GhostStopped(place.SlotIndex, place.X, place.Y);
            SendToRoomLocked(room, place, except: session);
            return null;
        }
    }

    // Passes on what a client tells of one of its own characters, in a match still running:
    // to everyone in the room, or back to the sender alone.
    private string? PassOn(ClientSession session, byte slot, IPacket packet, bool toSenderOnly)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            return PassOnLocked(session, slot, packet, toSenderOnly);
        }
    }

    private string? PassOnLocked(ClientSession session, byte slot, IPacket packet, bool toSenderOnly)
    {
        if (PlayingRoomLocked(session) is not { } room)
        {
            return NotInMatch;
        }

        var match = room.Match!;
        if (!match.IsPlayedBy(slot, session))
        {
            return $"slot {slot} is not one of its characters";
        }

        if (packet is PcGamePlayUseItem use)
        {
            match.RecordItemUse(use);
        }

        if (toSenderOnly)
        {
            session.Send(packet);
        }
        else
        {
            SendToRoomLocked(room, packet);
        }

        return null;
    }

    // The catch with the item the server draws, in a match between teams whose mode gives
    // items; as its client sent it otherwise, or when it names no item, as a client's catch
    // gives none in a mode without items.
    private GameScenarioCommandEatItemBox DrawnLocked(Room room, GameScenarioCommandEatItemBox caught) =>
        MatchRules.GivesItems(room.GameMode) && caught.PickupItemId != ItemDrops.NoItem
            ? caught with { PickupItemId = itemDrops.Draw(random) }
            : caught;

    // The room of a match still running in which the connection plays the character in slot,
    // and that character is out of the match: only a ghost moves.
    private Room? GhostRoomLocked(ClientSession session, byte slot, out string? why)
    {
        why = PlayingRoomLocked(session) is not { } room ? NotInMatch
            : !room.Match!.IsPlayedBy(slot, session) ? $"slot {slot} is not one of its characters"
            : room.Match.InPlay(slot) ? $"the character in slot {slot} is not a ghost"
            : null;
        return why is null ? _roomOf[session] : null;
    }
}
