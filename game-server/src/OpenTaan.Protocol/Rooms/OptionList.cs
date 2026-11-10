namespace OpenTaan.Protocol.Rooms;

/// <summary>The item options of the character in a slot, which only the empty list is read and written for.</summary>
/// <remarks>
/// The client looks the list up when a match starts without checking it is there, so
/// every occupied slot carries one, behind a presence flag. Nothing this server sends has
/// options in it yet, so it is always written empty, and a slot that carries none, or a
/// list holding any, is refused as <see cref="PacketError.Unsupported"/>.
/// </remarks>
[WireClass("OptionList")]
public sealed class OptionList
{
    private OptionList()
    {
    }

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<OptionList>.RegistryId;

    /// <summary>Reads the presence flag and the empty list behind it.</summary>
    internal static void ReadEmpty(ref PacketReader reader)
    {
        if (!reader.ReadBoolean())
        {
            reader.Fail(PacketError.Unsupported);
            return;
        }

        reader.ExpectRegistryId(RegistryId);
        if (reader.ReadUInt32() != 0)
        {
            reader.Fail(PacketError.Unsupported);
        }
    }

    /// <summary>Writes the presence flag and an empty list.</summary>
    internal static void WriteEmpty(PacketWriter writer)
    {
        writer.WriteBoolean(true);
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(0);
    }
}
