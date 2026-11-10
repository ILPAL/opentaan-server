using OpenTaan.GameServer.Characters;
using OpenTaan.GameServer.Sessions;

namespace OpenTaan.GameServer.Rooms;

/// <summary>A connection in a room, or asking to be in one, with the characters it plays.</summary>
/// <param name="Session">The connection.</param>
/// <param name="PlayerId">
/// The id the connection's login was answered with, which every slot it holds carries:
/// the client tells its own slots, and the host's, by it. See <see cref="ClientSession.PlayerId"/>.
/// </param>
/// <param name="ChannelId">The channel the connection is in, whose rooms it can be in.</param>
/// <param name="Characters">The characters it plays, in the order of its accounts; one slot each.</param>
public sealed record RoomMember(ClientSession Session, uint PlayerId, ushort ChannelId, IReadOnlyList<Character> Characters);
