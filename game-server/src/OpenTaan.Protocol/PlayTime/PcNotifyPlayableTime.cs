using System.Buffers;

namespace OpenTaan.Protocol.PlayTime;

/// <summary>Tells the client how long one account slot has played, and how long it may play.</summary>
/// <remarks>
/// <para>
/// The client keeps a play-time limit only in some regional builds, the Vietnamese one
/// among them. Such a client checks every character slot as it builds its lobby, and
/// quits, after a notice, for any slot whose <see cref="PlayedSeconds"/> has reached
/// <see cref="AllowedSeconds"/>. It knows neither value until this packet arrives, and
/// both start at 0, so a client of such a build that never receives this quits at its
/// first lobby.
/// </para>
/// <para>
/// The client keeps one value per account slot, for slots 0 and 1 only, and one
/// <see cref="AllowedSeconds"/> and <see cref="MaxPlaySeconds"/> for all of them: the
/// last packet's.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The account slot. The client ignores a packet for a slot above 1.</param>
/// <param name="Timestamp">When the server sent it, in seconds since 1970. The client does not read it.</param>
/// <param name="PlayedSeconds">How long the slot has played.</param>
/// <param name="AllowedSeconds">
/// How long any slot may play. The client quits at its lobby once a slot has played this
/// long, and its on-screen timer counts down to it.
/// </param>
/// <param name="MaxPlaySeconds">
/// How long a slot may play before the client warns: a slot already past it is warned
/// about as this arrives, and no longer counted as within its limit.
/// </param>
/// <param name="ProgressRatio">How much of the limit is used, from 0 to 1. The client shows it only in its warning, as a percentage.</param>
[WireClass("PcNotifyPlayableTime")]
public sealed record PcNotifyPlayableTime(
    byte SlotIndex,
    uint Timestamp,
    uint PlayedSeconds,
    uint AllowedSeconds,
    uint MaxPlaySeconds,
    float ProgressRatio) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcNotifyPlayableTime>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcNotifyPlayableTime> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var timestamp = reader.ReadUInt32();
        var playedSeconds = reader.ReadUInt32();
        var allowedSeconds = reader.ReadUInt32();
        var maxPlaySeconds = reader.ReadUInt32();
        var progressRatio = reader.ReadSingle();
        return reader.Finish(new PcNotifyPlayableTime(slotIndex, timestamp, playedSeconds, allowedSeconds, maxPlaySeconds, progressRatio));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteUInt32(Timestamp);
        writer.WriteUInt32(PlayedSeconds);
        writer.WriteUInt32(AllowedSeconds);
        writer.WriteUInt32(MaxPlaySeconds);
        writer.WriteSingle(ProgressRatio);
    }
}
