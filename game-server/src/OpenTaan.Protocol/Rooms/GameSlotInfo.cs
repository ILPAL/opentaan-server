using OpenTaan.Protocol.Characters;

namespace OpenTaan.Protocol.Rooms;

/// <summary>One of a room's slots: empty, open or closed, or holding a character.</summary>
/// <remarks>
/// <para>
/// A room has <see cref="SlotCount"/> slots. The client draws them four to a row, two rows,
/// the left two columns one team and the right two the other: slots 0, 1, 4 and 5 against
/// 2, 3, 6 and 7. It finds some slots by their index without checking it, so an index of
/// <see cref="SlotCount"/> or more can be neither read nor built.
/// </para>
/// <para>
/// Only an occupied slot carries more than its index and status: its
/// <see cref="Occupant"/>, present exactly when <see cref="Status"/> says the slot is taken.
/// </para>
/// </remarks>
/// <param name="SlotIndex">Which slot this is, below <see cref="SlotCount"/>.</param>
/// <param name="Status">What the slot holds.</param>
/// <param name="Occupant">The character in the slot, or null for an empty one.</param>
[WireClass("GameSlotInfo")]
public sealed record GameSlotInfo(byte SlotIndex, SlotStatus Status, SlotOccupant? Occupant)
{
    /// <summary>How many slots every room has.</summary>
    public const int SlotCount = 8;

    /// <summary>The fewest bytes a slot occupies: its id, index and status.</summary>
    internal const int MinimumSize = sizeof(uint) + (2 * sizeof(byte));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameSlotInfo>.RegistryId;

    /// <summary>Which slot this is, below <see cref="SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {SlotCount - 1}.");

    /// <summary>What the slot holds.</summary>
    public SlotStatus Status { get; } = Enum.IsDefined(Status)
        ? Status
        : throw new ArgumentOutOfRangeException(nameof(Status), $"{Status} is not a slot status.");

    /// <summary>The character in the slot, or null for an empty one.</summary>
    public SlotOccupant? Occupant { get; } = IsTaken(Status) == Occupant is not null
        ? Occupant
        : throw new ArgumentException("A slot holds a character exactly when its status says it is taken.", nameof(Occupant));

    /// <summary>An empty slot anyone entering may take.</summary>
    public static GameSlotInfo Open(byte slotIndex) => new(slotIndex, SlotStatus.Open, null);

    /// <summary>An empty slot the host has closed.</summary>
    public static GameSlotInfo Closed(byte slotIndex) => new(slotIndex, SlotStatus.Closed, null);

    /// <summary>A slot holding <paramref name="occupant"/>, ready to start or not.</summary>
    public static GameSlotInfo Taken(byte slotIndex, SlotOccupant occupant, bool ready) =>
        new(slotIndex, ready ? SlotStatus.Ready : SlotStatus.Seated, occupant);

    internal static GameSlotInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var status = (SlotStatus)reader.ReadByte();
        if (slotIndex >= SlotCount || !Enum.IsDefined(status))
        {
            reader.Fail(PacketError.UnexpectedValue);
            return Open(0);
        }

        if (!IsTaken(status))
        {
            return new GameSlotInfo(slotIndex, status, null);
        }

        // Null only when the read has failed, which the caller's Finish reports.
        return SlotOccupant.ReadFrom(ref reader) is { } occupant ? new GameSlotInfo(slotIndex, status, occupant) : Open(0);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte((byte)Status);
        Occupant?.WriteTo(writer);
    }

    private static bool IsTaken(SlotStatus status) => status >= SlotStatus.Seated;
}

/// <summary>The character in a slot, and the player whose character it is.</summary>
/// <remarks>
/// On the wire an occupied slot also carries the character's item options, after its
/// weapon. They are not part of this record: the list is always written empty, as every
/// occupied slot must carry one; see <see cref="OptionList"/>.
/// </remarks>
/// <param name="PlayerId">
/// The player's id: the one its login was answered with, which every slot of one
/// connection shares, so both characters of a connection logged in with two accounts
/// carry the same one. The client tells its own slots and the host's apart by it.
/// </param>
/// <param name="CharacterId">The character's own id.</param>
/// <param name="Name">The character's name.</param>
/// <param name="GuildName">The character's guild, or empty outside one.</param>
/// <param name="Level">The character's level.</param>
/// <param name="Team">The character's team, 1 or 2. The client uses it as an index without checking it, so no other value can be read or built.</param>
/// <param name="Appearance">How the character looks.</param>
/// <param name="WeaponId">The weapon the character has chosen for the match.</param>
public sealed record SlotOccupant(
    uint PlayerId,
    uint CharacterId,
    string Name,
    string GuildName,
    ushort Level,
    byte Team,
    CharacterAppear Appearance,
    ushort WeaponId)
{
    /// <summary>The character's name.</summary>
    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <summary>The character's guild, or empty outside one.</summary>
    public string GuildName { get; } = GuildName ?? throw new ArgumentNullException(nameof(GuildName));

    /// <summary>The character's team, 1 or 2.</summary>
    public byte Team { get; } = IsTeam(Team)
        ? Team
        : throw new ArgumentOutOfRangeException(nameof(Team), "A character is on team 1 or team 2.");

    /// <summary>How the character looks.</summary>
    public CharacterAppear Appearance { get; } = Appearance ?? throw new ArgumentNullException(nameof(Appearance));

    /// <summary>Reads the character, or returns null once the read has failed.</summary>
    internal static SlotOccupant? ReadFrom(ref PacketReader reader)
    {
        var playerId = reader.ReadUInt32();
        var characterId = reader.ReadUInt32();
        var name = reader.ReadUtf16String();
        var guildName = reader.ReadUtf16String();
        var level = reader.ReadUInt16();
        var team = reader.ReadByte();
        if (!IsTeam(team))
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        // Behind a presence flag, as an object of its own. Every slot this server sends
        // carries one, since the client draws the character from it.
        var appearance = reader.ReadBoolean() ? CharacterAppear.ReadFrom(ref reader) : null;
        if (appearance is null)
        {
            reader.Fail(PacketError.Unsupported);
        }

        var weaponId = reader.ReadUInt16();
        OptionList.ReadEmpty(ref reader);

        return reader.Error == PacketError.None
            ? new SlotOccupant(playerId, characterId, name, guildName, level, team, appearance!, weaponId)
            : null;
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(PlayerId);
        writer.WriteUInt32(CharacterId);
        writer.WriteUtf16String(Name);
        writer.WriteUtf16String(GuildName);
        writer.WriteUInt16(Level);
        writer.WriteByte(Team);
        writer.WriteBoolean(true);
        Appearance.WriteTo(writer);
        writer.WriteUInt16(WeaponId);
        OptionList.WriteEmpty(writer);
    }

    private static bool IsTeam(byte team) => team is 1 or 2;
}
