using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A mission's enemies take their turn: sent to everyone in the match, once to each connection.</summary>
/// <remarks>
/// <para>
/// The client's own name for it starts with Pq, though the server sends it. Mission enemies
/// never attack. Each client that receives it moves its own copy of the level's enemies:
/// most walk a little only if a shot hit them since their last turn, and some walk every
/// turn, each by an amount drawn from the match's seed, so every client moves them alike.
/// It then places the level's enemies whose wave is <see cref="Counter"/>, applies the
/// damage over time the enemies carry, and once every enemy stands still reports with
/// <see cref="PcGameSessionFinishMissionEnemyTurn"/>, carrying the same counter. A client
/// that misses one, or is sent one twice, has its enemies stand elsewhere from then on.
/// </para>
/// <para>
/// The client keeps the counter where it keeps the level's turn count, and shows it as
/// such until the next turn starts. Every level places its first enemies as wave 0 when it
/// begins, so a counter of 0 would place them all again.
/// </para>
/// </remarks>
/// <param name="Counter">The enemies' turn, which is also the wave of enemies placed: the level's turn count.</param>
[WireClass("PqGameSession_StartMissionEnemyTurn")]
public sealed record PqGameSessionStartMissionEnemyTurn(uint Counter) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionStartMissionEnemyTurn>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PqGameSessionStartMissionEnemyTurn> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var counter = reader.ReadUInt32();
        return reader.Finish(new PqGameSessionStartMissionEnemyTurn(counter));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(Counter);
    }
}
