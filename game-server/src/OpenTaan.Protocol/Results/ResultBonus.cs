namespace OpenTaan.Protocol.Results;

/// <summary>One bonus a character earned in a match, shown on the result screen.</summary>
/// <param name="BonusType">Which bonus it is.</param>
/// <param name="Experience">The experience it adds.</param>
/// <param name="Gold">The gold it adds.</param>
[WireClass("ResultBonus")]
public sealed record ResultBonus(byte BonusType, uint Experience, uint Gold)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + sizeof(byte) + (2 * sizeof(uint));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<ResultBonus>.RegistryId;

    internal static ResultBonus ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var bonusType = reader.ReadByte();
        var experience = reader.ReadUInt32();
        var gold = reader.ReadUInt32();
        return new ResultBonus(bonusType, experience, gold);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(BonusType);
        writer.WriteUInt32(Experience);
        writer.WriteUInt32(Gold);
    }
}
