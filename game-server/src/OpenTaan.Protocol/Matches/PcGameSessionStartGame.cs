using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A room's match starts: sent to everyone in the room, the answer to the host's <see cref="PqGameSessionStartGame"/>.</summary>
/// <remarks>
/// <para>
/// The client leaves the room's screen for the match on receiving it. In a room of teams
/// it loads the map this names; in a mission room it starts the mission this names and
/// loads that mission's own map, and a mission it does not have leaves it nothing to load.
/// </para>
/// <para>
/// Each character in the room starts as its entry in <see cref="Players"/> says. The
/// client looks the entries up by slot, so no two may name the same one.
/// </para>
/// </remarks>
/// <param name="MapId">The map the match is played on, or a mission's own number in a mission room.</param>
/// <param name="Players">How each character in the room starts, at most one entry for each slot.</param>
/// <param name="Wind">The wind the match starts with.</param>
/// <param name="Field24">A value the client keeps for its own use; the reference sessions carry 0.</param>
/// <param name="CaptainSlots">
/// For each team, by its number, the slot of the character who leads it, the first entry
/// unused: in matches between teams, the client lets the leader use items on teammates.
/// Always <see cref="CaptainCount"/> entries.
/// </param>
/// <param name="MissionSeed">The value every client lays a mission out from, so that all of them lay it out alike.</param>
[WireClass("PcGameSession_StartGame")]
public sealed record PcGameSessionStartGame(
    ushort MapId,
    IReadOnlyList<GameSlotPlayInfo> Players,
    uint Wind,
    ushort Field24,
    IReadOnlyList<byte> CaptainSlots,
    uint MissionSeed) : IPacket
{
    /// <summary>How many entries <see cref="CaptainSlots"/> has: one for each team number from 0.</summary>
    public const int CaptainCount = 3;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionStartGame>.RegistryId;

    /// <summary>How each character in the room starts, at most one entry for each slot.</summary>
    public IReadOnlyList<GameSlotPlayInfo> Players { get; } = NamesEachSlotOnce(Players ?? throw new ArgumentNullException(nameof(Players)))
        ? Players
        : throw new ArgumentException("No two characters start in the same slot.", nameof(Players));

    /// <summary>For each team, by its number, the slot of the character who leads it.</summary>
    public IReadOnlyList<byte> CaptainSlots { get; } = (CaptainSlots ?? throw new ArgumentNullException(nameof(CaptainSlots))).Count == CaptainCount
        ? CaptainSlots
        : throw new ArgumentException($"There is one entry for each of the {CaptainCount} team numbers.", nameof(CaptainSlots));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionStartGame> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var mapId = reader.ReadUInt16();
        var players = reader.ReadList(GameSlotPlayInfo.Size, GameSlotPlayInfo.ReadFrom);
        var wind = reader.ReadUInt32();
        var field24 = reader.ReadUInt16();
        var captainSlots = new byte[CaptainCount];
        for (var team = 0; team < CaptainCount; team++)
        {
            captainSlots[team] = reader.ReadByte();
        }

        var missionSeed = reader.ReadUInt32();
        if (!NamesEachSlotOnce(players))
        {
            reader.Fail(PacketError.UnexpectedValue);
            players = [];
        }

        return reader.Finish(new PcGameSessionStartGame(mapId, players, wind, field24, captainSlots, missionSeed));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(MapId);
        writer.WriteList(Players, static (list, player) => player.WriteTo(list));
        writer.WriteUInt32(Wind);
        writer.WriteUInt16(Field24);
        foreach (var slot in CaptainSlots)
        {
            writer.WriteByte(slot);
        }

        writer.WriteUInt32(MissionSeed);
    }

    /// <summary>Whether both start the same match: every field equal, and both lists equal in order.</summary>
    public bool Equals(PcGameSessionStartGame? other) =>
        other is not null
        && MapId == other.MapId
        && Players.SequenceEqual(other.Players)
        && Wind == other.Wind
        && Field24 == other.Field24
        && CaptainSlots.SequenceEqual(other.CaptainSlots)
        && MissionSeed == other.MissionSeed;

    /// <summary>A hash of the fields and every entry, consistent with <see cref="Equals(PcGameSessionStartGame)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MapId);
        foreach (var player in Players)
        {
            hash.Add(player);
        }

        hash.Add(Wind);
        hash.Add(Field24);
        foreach (var slot in CaptainSlots)
        {
            hash.Add(slot);
        }

        hash.Add(MissionSeed);
        return hash.ToHashCode();
    }

    private static bool NamesEachSlotOnce(IReadOnlyList<GameSlotPlayInfo> players) =>
        players.Select(player => player.SlotIndex).Distinct().Count() == players.Count;
}
