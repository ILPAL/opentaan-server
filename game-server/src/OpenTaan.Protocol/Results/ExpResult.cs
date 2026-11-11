namespace OpenTaan.Protocol.Results;

/// <summary>A character's level and experience at one step of a match's result, for the result screen's experience bar.</summary>
/// <param name="Level">The level.</param>
/// <param name="TotalExp">The experience the character has.</param>
/// <param name="LevelFloorExp">The experience at which the level begins.</param>
/// <param name="LevelCeilExp">The experience at which the next level begins.</param>
[WireClass("ExpResult")]
public sealed record ExpResult(ushort Level, uint TotalExp, uint LevelFloorExp, uint LevelCeilExp)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + sizeof(ushort) + (3 * sizeof(uint));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<ExpResult>.RegistryId;

    internal static ExpResult ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var level = reader.ReadUInt16();
        var totalExp = reader.ReadUInt32();
        var levelFloorExp = reader.ReadUInt32();
        var levelCeilExp = reader.ReadUInt32();
        return new ExpResult(level, totalExp, levelFloorExp, levelCeilExp);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt16(Level);
        writer.WriteUInt32(TotalExp);
        writer.WriteUInt32(LevelFloorExp);
        writer.WriteUInt32(LevelCeilExp);
    }
}
