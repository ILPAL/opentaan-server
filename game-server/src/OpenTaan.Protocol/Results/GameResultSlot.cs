using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Results;

/// <summary>One slot's row in a match's result: what its character earned and how it played.</summary>
/// <remarks>
/// <para>
/// The result screen shows a row for a character only when <see cref="CharacterLevel"/> is
/// neither 0 nor <see cref="NoCharacter"/>; a row for a slot with no character carries
/// <see cref="NoCharacter"/>, which also keeps the client from writing a level into the
/// room's slot. The character's name and team are not here: the client takes them from
/// the room.
/// </para>
/// <para>
/// The experience bar is drawn from <see cref="Levels"/>; only when that is empty does the
/// client show <see cref="ExpGained"/> instead, and nothing at all when that is
/// <see cref="NoExperienceShown"/>.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The slot, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="CharacterLevel">The character's level, or <see cref="NoCharacter"/> for a slot with no character.</param>
/// <param name="ExpGained">The experience earned, shown only when <see cref="Levels"/> is empty; <see cref="NoExperienceShown"/> shows none.</param>
/// <param name="Levels">The character's level and experience at each step of the experience bar, first to last.</param>
/// <param name="GoldEarned">The gold earned.</param>
/// <param name="KillStat">The kills, for the result screen.</param>
/// <param name="ForceStat">The damage dealt, for the result screen.</param>
/// <param name="HitRate">How many shots hit, from 0 to 1.</param>
/// <param name="SupportStat">The support given, for the result screen.</param>
/// <param name="Bonuses">The bonuses earned.</param>
/// <param name="ForceMedalAward">Force medals awarded, which the client adds to the character's own count.</param>
/// <param name="KillMedalAward">Kill medals awarded, which the client adds to the character's own count.</param>
/// <param name="SupportMedalAward">Support medals awarded, which the client adds to the character's own count.</param>
/// <param name="HasReward">Whether the character earned a reward beyond the match's.</param>
/// <param name="ExpReward">The experience that reward gives.</param>
/// <param name="GoldReward">The gold that reward gives.</param>
[WireClass("GameResultSlot")]
public sealed record GameResultSlot(
    byte SlotIndex,
    ushort CharacterLevel,
    int ExpGained,
    IReadOnlyList<ExpResult> Levels,
    uint GoldEarned,
    byte KillStat,
    uint ForceStat,
    float HitRate,
    uint SupportStat,
    IReadOnlyList<ResultBonus> Bonuses,
    byte ForceMedalAward,
    byte KillMedalAward,
    byte SupportMedalAward,
    bool HasReward,
    uint ExpReward,
    uint GoldReward)
{
    /// <summary>The level a row for a slot with no character carries.</summary>
    public const ushort NoCharacter = 0xFFFF;

    /// <summary>The experience earned that the client draws no number for.</summary>
    public const int NoExperienceShown = int.MaxValue;

    /// <summary>The fewest bytes a row occupies: every field, with both lists empty.</summary>
    internal const int MinimumSize = sizeof(uint) + sizeof(byte) + sizeof(ushort) + sizeof(int) + sizeof(uint) + sizeof(uint)
                                     + sizeof(byte) + (3 * sizeof(uint)) + sizeof(uint) + (4 * sizeof(byte)) + (2 * sizeof(uint));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameResultSlot>.RegistryId;

    /// <summary>The slot, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    /// <summary>The character's level and experience at each step of the experience bar, first to last.</summary>
    public IReadOnlyList<ExpResult> Levels { get; } = Levels ?? throw new ArgumentNullException(nameof(Levels));

    /// <summary>The bonuses earned.</summary>
    public IReadOnlyList<ResultBonus> Bonuses { get; } = Bonuses ?? throw new ArgumentNullException(nameof(Bonuses));

    /// <summary>The row for slot <paramref name="slotIndex"/> when it holds no character: nothing to show.</summary>
    public static GameResultSlot Empty(byte slotIndex) =>
        new(slotIndex, NoCharacter, 0, [], 0, 0, 0, 0, 0, [], 0, 0, 0, false, 0, 0);

    /// <summary>The row for a character of level <paramref name="level"/> in slot <paramref name="slotIndex"/> that earned nothing.</summary>
    public static GameResultSlot NothingEarned(byte slotIndex, ushort level) =>
        new(slotIndex, level, NoExperienceShown, [], 0, 0, 0, 0, 0, [], 0, 0, 0, false, 0, 0);

    /// <summary>Whether both rows say the same: every field equal, and both lists equal in order.</summary>
    public bool Equals(GameResultSlot? other) =>
        other is not null
        && SlotIndex == other.SlotIndex
        && CharacterLevel == other.CharacterLevel
        && ExpGained == other.ExpGained
        && Levels.SequenceEqual(other.Levels)
        && GoldEarned == other.GoldEarned
        && KillStat == other.KillStat
        && ForceStat == other.ForceStat
        && HitRate.Equals(other.HitRate)
        && SupportStat == other.SupportStat
        && Bonuses.SequenceEqual(other.Bonuses)
        && ForceMedalAward == other.ForceMedalAward
        && KillMedalAward == other.KillMedalAward
        && SupportMedalAward == other.SupportMedalAward
        && HasReward == other.HasReward
        && ExpReward == other.ExpReward
        && GoldReward == other.GoldReward;

    /// <summary>A hash of the fields and every entry, consistent with <see cref="Equals(GameResultSlot)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SlotIndex);
        hash.Add(CharacterLevel);
        hash.Add(ExpGained);
        foreach (var level in Levels)
        {
            hash.Add(level);
        }

        hash.Add(GoldEarned);
        hash.Add(KillStat);
        hash.Add(ForceStat);
        hash.Add(HitRate);
        hash.Add(SupportStat);
        foreach (var bonus in Bonuses)
        {
            hash.Add(bonus);
        }

        hash.Add(ForceMedalAward);
        hash.Add(KillMedalAward);
        hash.Add(SupportMedalAward);
        hash.Add(HasReward);
        hash.Add(ExpReward);
        hash.Add(GoldReward);
        return hash.ToHashCode();
    }

    internal static GameResultSlot ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var characterLevel = reader.ReadUInt16();
        var expGained = reader.ReadInt32();
        var levels = reader.ReadList(ExpResult.Size, ExpResult.ReadFrom);
        var goldEarned = reader.ReadUInt32();
        var killStat = reader.ReadByte();
        var forceStat = reader.ReadUInt32();
        var hitRate = reader.ReadSingle();
        var supportStat = reader.ReadUInt32();
        var bonuses = reader.ReadList(ResultBonus.Size, ResultBonus.ReadFrom);
        var forceMedalAward = reader.ReadByte();
        var killMedalAward = reader.ReadByte();
        var supportMedalAward = reader.ReadByte();
        var hasReward = reader.ReadBoolean();
        var expReward = reader.ReadUInt32();
        var goldReward = reader.ReadUInt32();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return new GameResultSlot(
            slotIndex,
            characterLevel,
            expGained,
            levels,
            goldEarned,
            killStat,
            forceStat,
            hitRate,
            supportStat,
            bonuses,
            forceMedalAward,
            killMedalAward,
            supportMedalAward,
            hasReward,
            expReward,
            goldReward);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteUInt16(CharacterLevel);
        writer.WriteInt32(ExpGained);
        writer.WriteList(Levels, static (list, level) => level.WriteTo(list));
        writer.WriteUInt32(GoldEarned);
        writer.WriteByte(KillStat);
        writer.WriteUInt32(ForceStat);
        writer.WriteSingle(HitRate);
        writer.WriteUInt32(SupportStat);
        writer.WriteList(Bonuses, static (list, bonus) => bonus.WriteTo(list));
        writer.WriteByte(ForceMedalAward);
        writer.WriteByte(KillMedalAward);
        writer.WriteByte(SupportMedalAward);
        writer.WriteBoolean(HasReward);
        writer.WriteUInt32(ExpReward);
        writer.WriteUInt32(GoldReward);
    }
}
