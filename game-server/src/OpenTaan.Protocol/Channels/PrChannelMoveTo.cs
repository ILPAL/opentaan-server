using System.Buffers;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Protocol.Channels;

/// <summary>The answer to <see cref="PqChannelMoveTo"/>.</summary>
/// <param name="Result">Whether the player entered, must reconnect, or was refused, and why.</param>
/// <param name="ChannelId">The channel asked for.</param>
/// <param name="ServerAddress">Where to reconnect, read only for <see cref="ChannelMoveResult.Reconnect"/>.</param>
/// <param name="Characters">The characters that move, read only for <see cref="ChannelMoveResult.Reconnect"/>.</param>
/// <param name="Channel">
/// The channel. The client keeps it on <see cref="ChannelMoveResult.Entered"/>, and shows
/// its level range on <see cref="ChannelMoveResult.LevelTooHigh"/> and
/// <see cref="ChannelMoveResult.LevelTooLow"/>; it is null otherwise.
/// </param>
[WireClass("PrChannel_MoveTo")]
public sealed record PrChannelMoveTo(
    ChannelMoveResult Result,
    ushort ChannelId,
    ConnectionAddress ServerAddress,
    IReadOnlyList<MovingCharacter> Characters,
    ChannelInfo? Channel) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrChannelMoveTo>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrChannelMoveTo> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var result = (ChannelMoveResult)reader.ReadByte();
        var channelId = reader.ReadUInt16();
        var serverAddress = ConnectionAddress.ReadFrom(ref reader);
        var characters = reader.ReadList(MovingCharacter.Size, MovingCharacter.ReadFrom);
        var channel = reader.ReadBoolean() ? ChannelInfo.ReadFrom(ref reader) : null;
        return reader.Finish(new PrChannelMoveTo(result, channelId, serverAddress, characters, channel));
    }

    /// <summary>Writes the body.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Result"/> is not one the enum names.</exception>
    public void Write(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(ServerAddress);
        DialogStatus.EnsureKnown(Result);

        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Result);
        writer.WriteUInt16(ChannelId);
        ServerAddress.WriteTo(writer);
        writer.WriteList(Characters, static (list, character) => character.WriteTo(list));
        writer.WriteBoolean(Channel is not null);
        Channel?.WriteTo(writer);
    }
}

/// <summary>A character that moves to another channel, with its account.</summary>
/// <param name="AccountId">The account's id.</param>
/// <param name="CharacterId">The character's id.</param>
public readonly record struct MovingCharacter(uint AccountId, uint CharacterId)
{
    /// <summary>Bytes it occupies.</summary>
    internal const int Size = 2 * sizeof(uint);

    internal static MovingCharacter ReadFrom(ref PacketReader reader) => new(reader.ReadUInt32(), reader.ReadUInt32());

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(AccountId);
        writer.WriteUInt32(CharacterId);
    }
}
