using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Protocol.Lobby;

/// <summary>A character in a room, as the lobby shows it before entering: who, at what level, and of which gender.</summary>
/// <param name="CharacterId">The character's id.</param>
/// <param name="Name">The character's name.</param>
/// <param name="Level">The character's level.</param>
/// <param name="Gender">The character's gender, which is its account's.</param>
[WireClass("UserSimpleInfo")]
public sealed record UserSimpleInfo(uint CharacterId, string Name, ushort Level, Gender Gender)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<UserSimpleInfo>.RegistryId;

    /// <summary>The fewest bytes it occupies, with an empty name: its id, the character's id, the name's count, the level and the gender.</summary>
    internal const int MinimumSize = (3 * sizeof(uint)) + sizeof(ushort) + sizeof(byte);

    /// <summary>The character's name.</summary>
    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    internal static UserSimpleInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var name = reader.ReadUtf16String();
        var level = reader.ReadUInt16();
        var gender = (Gender)reader.ReadByte();
        return new UserSimpleInfo(characterId, name, level, gender);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUtf16String(Name);
        writer.WriteUInt16(Level);
        writer.WriteByte((byte)Gender);
    }
}
