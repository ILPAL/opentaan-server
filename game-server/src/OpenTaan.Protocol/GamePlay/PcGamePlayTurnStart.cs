using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A turn starts: sent to everyone in the match, naming the character whose turn it is.</summary>
/// <remarks>
/// <para>
/// The client acts on it in whatever state it is in, the end of a match included, so it
/// is never sent once a match has ended. It looks the turn's character up by slot without
/// checking the index, so the slot is always one of a room's.
/// </para>
/// <para>
/// In a mission it also ends whatever the client's mission was doing, the opening of a
/// level included, which is why a level's first turn waits until every client has
/// finished the opening.
/// </para>
/// </remarks>
/// <param name="TurnSlot">The slot of the character whose turn it is, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="Field11">A value the client keeps and never reads; the reference sessions carry 0.</param>
/// <param name="Wind">The wind for the turn.</param>
/// <param name="AmmoAddition">Ammunition the turn's character is given.</param>
/// <param name="ItemBoxBudget">The most item boxes the turn's shot may drop.</param>
/// <param name="RemovedItemBoxes">The item boxes taken off the field, by their ids.</param>
/// <param name="GhostMoves">Ghosts moving from one place to another.</param>
/// <param name="HpUpdates">Changes to characters' health.</param>
/// <param name="DropQuality">How good the items the turn drops are, before each team's share.</param>
/// <param name="DropQualityByTeam">What each team adds to <see cref="DropQuality"/>, by team number from 0: always <see cref="TeamCount"/> entries.</param>
/// <param name="MissionRound">In a mission, the round of the level the turn belongs to; read only there.</param>
[WireClass("PcGamePlay_TurnStart")]
public sealed record PcGamePlayTurnStart(
    byte TurnSlot,
    byte Field11,
    uint Wind,
    ushort AmmoAddition,
    byte ItemBoxBudget,
    IReadOnlyList<ulong> RemovedItemBoxes,
    IReadOnlyList<GhostMove> GhostMoves,
    IReadOnlyList<HpUpdate> HpUpdates,
    uint DropQuality,
    IReadOnlyList<uint> DropQualityByTeam,
    ushort MissionRound) : IPacket
{
    /// <summary>How many entries <see cref="DropQualityByTeam"/> has: one for each team number from 0.</summary>
    public const int TeamCount = 3;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayTurnStart>.RegistryId;

    /// <summary>The slot of the character whose turn it is, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte TurnSlot { get; } = TurnSlot < GameSlotInfo.SlotCount
        ? TurnSlot
        : throw new ArgumentOutOfRangeException(nameof(TurnSlot), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The item boxes taken off the field, by their ids.</summary>
    public IReadOnlyList<ulong> RemovedItemBoxes { get; } = RemovedItemBoxes ?? throw new ArgumentNullException(nameof(RemovedItemBoxes));

    /// <summary>Ghosts moving from one place to another.</summary>
    public IReadOnlyList<GhostMove> GhostMoves { get; } = GhostMoves ?? throw new ArgumentNullException(nameof(GhostMoves));

    /// <summary>Changes to characters' health.</summary>
    public IReadOnlyList<HpUpdate> HpUpdates { get; } = HpUpdates ?? throw new ArgumentNullException(nameof(HpUpdates));

    /// <summary>What each team adds to <see cref="DropQuality"/>, by team number from 0.</summary>
    public IReadOnlyList<uint> DropQualityByTeam { get; } = (DropQualityByTeam ?? throw new ArgumentNullException(nameof(DropQualityByTeam))).Count == TeamCount
        ? DropQualityByTeam
        : throw new ArgumentException($"There is one entry for each of the {TeamCount} team numbers.", nameof(DropQualityByTeam));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGamePlayTurnStart> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var turnSlot = reader.ReadByte();
        var field11 = reader.ReadByte();
        var wind = reader.ReadUInt32();
        var ammoAddition = reader.ReadUInt16();
        var itemBoxBudget = reader.ReadByte();
        var removedItemBoxes = reader.ReadList(sizeof(ulong), static (ref PacketReader list) => list.ReadUInt64());
        var ghostMoves = reader.ReadList(GhostMove.Size, GhostMove.ReadFrom);
        var hpUpdates = reader.ReadList(HpUpdate.Size, HpUpdate.ReadFrom);
        var dropQuality = reader.ReadUInt32();
        var dropQualityByTeam = new uint[TeamCount];
        for (var team = 0; team < TeamCount; team++)
        {
            dropQualityByTeam[team] = reader.ReadUInt32();
        }

        var missionRound = reader.ReadUInt16();
        if (turnSlot >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            turnSlot = 0;
        }

        return reader.Finish(new PcGamePlayTurnStart(
            turnSlot,
            field11,
            wind,
            ammoAddition,
            itemBoxBudget,
            removedItemBoxes,
            ghostMoves,
            hpUpdates,
            dropQuality,
            dropQualityByTeam,
            missionRound));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(TurnSlot);
        writer.WriteByte(Field11);
        writer.WriteUInt32(Wind);
        writer.WriteUInt16(AmmoAddition);
        writer.WriteByte(ItemBoxBudget);
        writer.WriteList(RemovedItemBoxes, static (list, id) => list.WriteUInt64(id));
        writer.WriteList(GhostMoves, static (list, move) => move.WriteTo(list));
        writer.WriteList(HpUpdates, static (list, update) => update.WriteTo(list));
        writer.WriteUInt32(DropQuality);
        foreach (var share in DropQualityByTeam)
        {
            writer.WriteUInt32(share);
        }

        writer.WriteUInt16(MissionRound);
    }

    /// <summary>Whether both start the same turn: every field equal, and every list equal in order.</summary>
    public bool Equals(PcGamePlayTurnStart? other) =>
        other is not null
        && TurnSlot == other.TurnSlot
        && Field11 == other.Field11
        && Wind == other.Wind
        && AmmoAddition == other.AmmoAddition
        && ItemBoxBudget == other.ItemBoxBudget
        && RemovedItemBoxes.SequenceEqual(other.RemovedItemBoxes)
        && GhostMoves.SequenceEqual(other.GhostMoves)
        && HpUpdates.SequenceEqual(other.HpUpdates)
        && DropQuality == other.DropQuality
        && DropQualityByTeam.SequenceEqual(other.DropQualityByTeam)
        && MissionRound == other.MissionRound;

    /// <summary>A hash of the fields and every entry, consistent with <see cref="Equals(PcGamePlayTurnStart)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TurnSlot);
        hash.Add(Field11);
        hash.Add(Wind);
        hash.Add(AmmoAddition);
        hash.Add(ItemBoxBudget);
        AddAll(ref hash, RemovedItemBoxes);
        AddAll(ref hash, GhostMoves);
        AddAll(ref hash, HpUpdates);
        hash.Add(DropQuality);
        AddAll(ref hash, DropQualityByTeam);
        hash.Add(MissionRound);
        return hash.ToHashCode();
    }

    private static void AddAll<T>(ref HashCode hash, IEnumerable<T> elements)
    {
        foreach (var element in elements)
        {
            hash.Add(element);
        }
    }
}
