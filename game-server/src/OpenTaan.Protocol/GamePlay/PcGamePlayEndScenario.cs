using System.Buffers;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A client has finished playing a shot back.</summary>
/// <remarks>
/// <para>
/// Every client that plays a shot sends it, the shooter's included, and sends it twice:
/// once when it reaches the mark the shooter's client puts at the end of every shot, and
/// once more when the last step is behind it. So the server counts each client once, and
/// ends the turn once every client has finished.
/// </para>
/// <para>
/// The firing slot is the one the shot was sent for, copied by the client as it came, so
/// it is read as it comes too, and the server compares it with the shot it passed on.
/// </para>
/// </remarks>
/// <param name="FiringSlot">The slot of the character whose shot was played.</param>
/// <param name="CompletionStatus">How the playback ended; the client always sends 0.</param>
[WireClass("PcGamePlay_EndScenario")]
public sealed record PcGamePlayEndScenario(byte FiringSlot, byte CompletionStatus) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayEndScenario>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayEndScenario> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var firingSlot = reader.ReadByte();
        var completionStatus = reader.ReadByte();
        return reader.Finish(new PcGamePlayEndScenario(firingSlot, completionStatus));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(FiringSlot);
        writer.WriteByte(CompletionStatus);
    }
}
