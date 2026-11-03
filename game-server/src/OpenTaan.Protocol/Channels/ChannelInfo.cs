using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Protocol.Channels;

/// <summary>A channel, as the client keeps the one it is in.</summary>
/// <remarks>
/// Fields whose meaning is not established yet are named after where the client keeps
/// them, such as <see cref="Field1C"/>.
/// </remarks>
/// <param name="Name">The channel's name, shown in the lobby.</param>
/// <param name="ChannelId">The channel's id.</param>
/// <param name="Mode">What kind of channel it is; arena channels have modes of their own.</param>
/// <param name="ServerAddress">The server the channel is on.</param>
/// <param name="FillPercent">How full the channel is, in percent. At 100 or more the client calls it full and does not try to enter.</param>
/// <param name="Field1C">Not read by the client as far as is known.</param>
/// <param name="MinimumLevel">The lowest level that may enter, or 0 for none.</param>
/// <param name="MaximumLevel">The highest level that may enter, or 0 for none.</param>
/// <param name="Disabled">Non-zero when the channel is closed; the client then does not try to enter.</param>
[WireClass("ChannelInfo")]
public sealed record ChannelInfo(
    string Name,
    ushort ChannelId,
    byte Mode,
    ConnectionAddress ServerAddress,
    uint FillPercent,
    uint Field1C,
    ushort MinimumLevel,
    ushort MaximumLevel,
    byte Disabled)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<ChannelInfo>.RegistryId;

    /// <summary>The fewest bytes it can occupy: its id and every field, with an empty name.</summary>
    internal const int MinimumSize = sizeof(uint) + sizeof(uint) + sizeof(ushort) + sizeof(byte) + ConnectionAddress.Size
        + (2 * sizeof(uint)) + (2 * sizeof(ushort)) + sizeof(byte);

    internal static ChannelInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var name = reader.ReadUtf16String();
        var channelId = reader.ReadUInt16();
        var mode = reader.ReadByte();
        var serverAddress = ConnectionAddress.ReadFrom(ref reader);
        var fillPercent = reader.ReadUInt32();
        var field1C = reader.ReadUInt32();
        var minimumLevel = reader.ReadUInt16();
        var maximumLevel = reader.ReadUInt16();
        var disabled = reader.ReadByte();
        return new ChannelInfo(name, channelId, mode, serverAddress, fillPercent, field1C, minimumLevel, maximumLevel, disabled);
    }

    internal void WriteTo(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(ServerAddress);

        writer.WriteUInt32(RegistryId);
        writer.WriteUtf16String(Name);
        writer.WriteUInt16(ChannelId);
        writer.WriteByte(Mode);
        ServerAddress.WriteTo(writer);
        writer.WriteUInt32(FillPercent);
        writer.WriteUInt32(Field1C);
        writer.WriteUInt16(MinimumLevel);
        writer.WriteUInt16(MaximumLevel);
        writer.WriteByte(Disabled);
    }
}
