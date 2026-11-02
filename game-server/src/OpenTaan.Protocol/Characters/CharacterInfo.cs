using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Protocol.Characters;

/// <summary>A character's name, progress and record, as the character lists carry it.</summary>
/// <remarks>
/// The client draws its experience bar as
/// <c>(CurrentExperience - ExperienceFloor) / (ExperienceCeiling - ExperienceFloor)</c>
/// with no guard against a zero width, so the ceiling must always be above the floor.
/// </remarks>
/// <param name="CharacterId">The character's id, which selection and deletion send back.</param>
/// <param name="Name">The character's name.</param>
/// <param name="Level">The character's level.</param>
/// <param name="CurrentExperience">Experience gathered in total.</param>
/// <param name="ExperienceFloor">The total at which the current level began.</param>
/// <param name="ExperienceCeiling">The total at which the next level begins. Always above the floor.</param>
/// <param name="Gold">Gold, the everyday currency.</param>
/// <param name="Gender">The character's gender, which is the account's.</param>
/// <param name="RankPlacement">Position in the ranking.</param>
/// <param name="RankScore">Ranking points gathered in total.</param>
/// <param name="LeagueMoney">League money, the currency of the league shop.</param>
/// <param name="SoulPoints">Soul points. Not experience.</param>
/// <param name="Wins">Matches won.</param>
/// <param name="Losses">Matches lost.</param>
/// <param name="ForceMedals">Force medals awarded in total.</param>
/// <param name="KillMedals">Kill medals awarded in total.</param>
/// <param name="SupportMedals">Support medals awarded in total.</param>
/// <param name="GuildName">The guild's name, or empty outside a guild.</param>
[WireClass("CharacterInfo")]
public sealed record CharacterInfo(
    uint CharacterId,
    string Name,
    ushort Level,
    uint CurrentExperience,
    uint ExperienceFloor,
    uint ExperienceCeiling,
    uint Gold,
    Gender Gender,
    uint RankPlacement,
    uint RankScore,
    uint LeagueMoney,
    uint SoulPoints,
    uint Wins,
    uint Losses,
    uint ForceMedals,
    uint KillMedals,
    uint SupportMedals,
    string GuildName)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<CharacterInfo>.RegistryId;

    /// <summary>
    /// The fewest bytes it can occupy, with both names empty: the id and fourteen 32-bit
    /// fields, the level, the gender, and the two names' counts.
    /// </summary>
    internal const int MinimumSize = (15 * sizeof(uint)) + sizeof(ushort) + sizeof(byte) + (2 * sizeof(uint));

    internal static CharacterInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var characterId = reader.ReadUInt32();
        var name = reader.ReadUtf16String();
        var level = reader.ReadUInt16();
        var currentExperience = reader.ReadUInt32();
        var experienceFloor = reader.ReadUInt32();
        var experienceCeiling = reader.ReadUInt32();
        var gold = reader.ReadUInt32();
        var gender = (Gender)reader.ReadByte();
        var rankPlacement = reader.ReadUInt32();
        var rankScore = reader.ReadUInt32();
        var leagueMoney = reader.ReadUInt32();
        var soulPoints = reader.ReadUInt32();
        var wins = reader.ReadUInt32();
        var losses = reader.ReadUInt32();
        var forceMedals = reader.ReadUInt32();
        var killMedals = reader.ReadUInt32();
        var supportMedals = reader.ReadUInt32();
        var guildName = reader.ReadUtf16String();
        return new CharacterInfo(
            characterId, name, level, currentExperience, experienceFloor, experienceCeiling, gold, gender,
            rankPlacement, rankScore, leagueMoney, soulPoints, wins, losses, forceMedals, killMedals, supportMedals,
            guildName);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUtf16String(Name);
        writer.WriteUInt16(Level);
        writer.WriteUInt32(CurrentExperience);
        writer.WriteUInt32(ExperienceFloor);
        writer.WriteUInt32(ExperienceCeiling);
        writer.WriteUInt32(Gold);
        writer.WriteByte((byte)Gender);
        writer.WriteUInt32(RankPlacement);
        writer.WriteUInt32(RankScore);
        writer.WriteUInt32(LeagueMoney);
        writer.WriteUInt32(SoulPoints);
        writer.WriteUInt32(Wins);
        writer.WriteUInt32(Losses);
        writer.WriteUInt32(ForceMedals);
        writer.WriteUInt32(KillMedals);
        writer.WriteUInt32(SupportMedals);
        writer.WriteUtf16String(GuildName);
    }
}
