using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A client in a mission reports that the enemies' turn is over on its screen.</summary>
/// <remarks>
/// The client's own name for it starts with Pc, though the client sends it. A client sends
/// it once for each <see cref="PqGameSessionStartMissionEnemyTurn"/> it is sent, however
/// many characters it plays, once every enemy stands still, and then waits for the next
/// turn to start.
/// </remarks>
/// <param name="Counter">The counter of the enemies' turn it reports, as it was sent.</param>
[WireClass("PcGameSession_FinishMissionEnemyTurn")]
public sealed record PcGameSessionFinishMissionEnemyTurn(uint Counter) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionFinishMissionEnemyTurn>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGameSessionFinishMissionEnemyTurn> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var counter = reader.ReadUInt32();
        return reader.Finish(new PcGameSessionFinishMissionEnemyTurn(counter));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(Counter);
    }
}
