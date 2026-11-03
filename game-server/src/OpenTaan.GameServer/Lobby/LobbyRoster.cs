using System.Buffers;
using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Transport;

namespace OpenTaan.GameServer.Lobby;

/// <summary>Who is in each channel's lobby, and what everyone in it has been told. Shared by every connection.</summary>
/// <remarks>
/// <para>
/// The client shows a lobby's players from the list it is sent on entering, then adds a
/// player for every add it is sent and removes one for every removal, never checking
/// what it already shows. So each player in a lobby is added to everyone else's list
/// once when it enters, and removed once however it goes: by leaving, by moving to a
/// channel, or by its connection closing.
/// </para>
/// <para>
/// Listing a lobby for the player entering it, adding that player, and telling everyone
/// else are one step under one lock, as is a removal. So two players entering at once
/// each see the other exactly once, and every player in a lobby is told of the same
/// comings and goings in the same order. Sending only queues a packet, so nothing under
/// the lock waits on a network.
/// </para>
/// </remarks>
public sealed class LobbyRoster
{
    private static readonly int EmptyListSize = SizeOf(new PcLobbyInitUserList([], []));
    private static readonly int EmptyUpdateSize = SizeOf(PcLobbyUpdateUserList.Adding([], []));

    private readonly Dictionary<ClientSession, Member> _members = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Puts <paramref name="session"/> in the lobby of <paramref name="channelId"/> with
    /// the characters it plays, and sends it the lobby's players, itself included.
    /// </summary>
    /// <remarks>
    /// A connection in another lobby leaves that one first. One already in this lobby is
    /// sent the list again and added to no one's a second time.
    /// </remarks>
    public void Enter(ClientSession session, ushort channelId, IReadOnlyList<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(characters);

        lock (_lock)
        {
            if (!_members.TryGetValue(session, out var member) || member.ChannelId != channelId)
            {
                LeaveLocked(session);
                member = new Member(channelId, characters);
                var added = PcLobbyUpdateUserList.Adding(
                    [.. characters.Select(character => character.Info)],
                    [.. characters.Select(character => character.Appearance)]);
                SendToLobbyLocked(channelId, added);
                _members[session] = member;
            }

            foreach (var packet in ListOf([.. InLobbyLocked(channelId).SelectMany(other => other.Characters)]))
            {
                session.Send(packet);
            }
        }
    }

    /// <summary>Takes <paramref name="session"/> out of its lobby, if it is in one, and tells everyone left in it.</summary>
    public void Leave(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_lock)
        {
            LeaveLocked(session);
        }
    }

    /// <summary>
    /// The list a player entering a lobby with <paramref name="characters"/> in it is sent:
    /// one list, and when that would not fit a frame, as many adds after it as the rest needs.
    /// </summary>
    public static IReadOnlyList<IPacket> ListOf(IReadOnlyList<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);

        var packets = new List<IPacket>();
        var start = 0;
        do
        {
            var budget = TransportLimits.MaxEncryptedBody - (start == 0 ? EmptyListSize : EmptyUpdateSize);
            var end = start;
            while (end < characters.Count && (budget -= SizeOf(characters[end])) >= 0)
            {
                end++;
            }

            // A character too large for a frame of its own still goes, and the send refuses it.
            end = Math.Max(end, Math.Min(start + 1, characters.Count));

            var slice = characters.Skip(start).Take(end - start).ToArray();
            var infos = slice.Select(character => character.Info).ToArray();
            var appearances = slice.Select(character => character.Appearance).ToArray();
            packets.Add(start == 0 ? new PcLobbyInitUserList(infos, appearances) : PcLobbyUpdateUserList.Adding(infos, appearances));
            start = end;
        }
        while (start < characters.Count);

        return packets;
    }

    private void LeaveLocked(ClientSession session)
    {
        if (_members.Remove(session, out var member))
        {
            SendToLobbyLocked(member.ChannelId, PcLobbyUpdateUserList.Removing([.. member.Characters.Select(character => character.CharacterId)]));
        }
    }

    // A copy, so the lobby can change while it is being told: a send that finds a
    // connection's queue full closes that connection.
    private void SendToLobbyLocked(ushort channelId, IPacket packet)
    {
        foreach (var (session, _) in _members.Where(entry => entry.Value.ChannelId == channelId).ToArray())
        {
            session.Send(packet);
        }
    }

    private IEnumerable<Member> InLobbyLocked(ushort channelId) => _members.Values.Where(member => member.ChannelId == channelId);

    // What a character adds to a list.
    private static int SizeOf(Character character) =>
        SizeOf(new PcLobbyInitUserList([character.Info], [character.Appearance])) - EmptyListSize;

    private static int SizeOf(IPacket packet)
    {
        var body = new ArrayBufferWriter<byte>();
        packet.Write(body);
        return body.WrittenCount;
    }

    private sealed record Member(ushort ChannelId, IReadOnlyList<Character> Characters);
}
