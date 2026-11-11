using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A turn ends: sent to everyone in the match, before the start of the next turn.</summary>
/// <remarks>
/// <para>
/// The client looks the turn's character up by slot without checking the index, so the
/// slot is always one of a room's; a slot whose character has left is skipped. It may
/// carry a change to a team's gauge, written as a flag and then, when the flag is set,
/// the change itself.
/// </para>
/// <para>
/// It is never sent after a shot that begins a level: the client's change of level would
/// be broken by it. The level's first turn follows once every client has played the
/// level's opening.
/// </para>
/// </remarks>
/// <param name="TurnSlot">The slot of the character whose turn ends, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="TurnSeed">A value the client keeps for the character; the reference sessions carry 0.</param>
/// <param name="TeamGauge">A change to a team's gauge, or null for none.</param>
[WireClass("PcGamePlay_TurnEnd")]
public sealed record PcGamePlayTurnEnd(byte TurnSlot, uint TurnSeed, GameScenarioCommandTeamGauge? TeamGauge) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayTurnEnd>.RegistryId;

    /// <summary>The slot of the character whose turn ends, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte TurnSlot { get; } = TurnSlot < GameSlotInfo.SlotCount
        ? TurnSlot
        : throw new ArgumentOutOfRangeException(nameof(TurnSlot), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGamePlayTurnEnd> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var turnSlot = reader.ReadByte();
        var turnSeed = reader.ReadUInt32();
        var teamGauge = TeamGaugeChange.ReadOptional(ref reader);
        if (turnSlot >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            turnSlot = 0;
        }

        return reader.Finish(new PcGamePlayTurnEnd(turnSlot, turnSeed, teamGauge));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(TurnSlot);
        writer.WriteUInt32(TurnSeed);
        TeamGaugeChange.WriteOptional(writer, TeamGauge);
    }
}
