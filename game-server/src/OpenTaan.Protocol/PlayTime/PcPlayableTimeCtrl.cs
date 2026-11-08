using System.Buffers;

namespace OpenTaan.Protocol.PlayTime;

/// <summary>Sets how long one character's slot has played, and whether the client counts its time.</summary>
/// <remarks>
/// The client finds the account slot by the character, and ignores a character it has not
/// selected. <see cref="PlayedSeconds"/> replaces the slot's played time, the same value
/// <see cref="PcNotifyPlayableTime.PlayedSeconds"/> sets. While <see cref="Counting"/> is
/// on, the client adds each second played to it, and at fixed marks shows in chat how
/// long is left.
/// </remarks>
/// <param name="CharacterId">The character whose slot this is for.</param>
/// <param name="PlayedSeconds">How long the slot has played.</param>
/// <param name="Counting">Whether the client counts the slot's time from now on.</param>
[WireClass("PcPlayableTimeCtrl")]
public sealed record PcPlayableTimeCtrl(uint CharacterId, uint PlayedSeconds, bool Counting) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcPlayableTimeCtrl>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcPlayableTimeCtrl> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var playedSeconds = reader.ReadUInt32();
        var counting = reader.ReadBoolean();
        return reader.Finish(new PcPlayableTimeCtrl(characterId, playedSeconds, counting));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUInt32(PlayedSeconds);
        writer.WriteBoolean(Counting);
    }
}
