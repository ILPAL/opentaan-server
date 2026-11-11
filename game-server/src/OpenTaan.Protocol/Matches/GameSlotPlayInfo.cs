using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Matches;

/// <summary>How one character starts a match: where it stands, its health, its weapon and its ammunition.</summary>
/// <remarks>
/// <para>
/// The start of a match carries one of these for each character in the room. The client
/// binds each to the room's slot of the same index, and only for a slot that holds a
/// character: an entry for an empty or closed slot is ignored, and a character with no
/// entry is left without the data it plays with.
/// </para>
/// <para>
/// The health given here is the character's health for the whole match, until a later
/// packet changes it: the client keeps no health from one match to the next.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The room's slot the character is in, below <see cref="GameSlotInfo.SlotCount"/>.</param>
/// <param name="X">Where the character starts, across the map.</param>
/// <param name="Y">Where the character starts, down the map.</param>
/// <param name="IsAirborne">Whether the character starts in the air.</param>
/// <param name="Hp">The character's health at the start.</param>
/// <param name="MaxHp">The most health the character can have.</param>
/// <param name="Seed">A value of the character's own, kept by the client; the reference sessions carry 0.</param>
/// <param name="WeaponId">The weapon the character plays with.</param>
/// <param name="MaxAmmo">The most ammunition the character's weapon holds.</param>
/// <param name="Field3C">A value the client keeps and never reads while playing; the reference sessions carry 0.</param>
/// <param name="StartingAmmo">The ammunition the character starts with, added to none.</param>
[WireClass("GameSlotPlayInfo")]
public sealed record GameSlotPlayInfo(
    byte SlotIndex,
    int X,
    int Y,
    bool IsAirborne,
    uint Hp,
    uint MaxHp,
    uint Seed,
    ushort WeaponId,
    ushort MaxAmmo,
    ushort Field3C,
    ushort StartingAmmo)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + sizeof(byte) + (2 * sizeof(int)) + sizeof(byte) + (3 * sizeof(uint)) + (4 * sizeof(ushort));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameSlotPlayInfo>.RegistryId;

    /// <summary>The room's slot the character is in, below <see cref="GameSlotInfo.SlotCount"/>.</summary>
    public byte SlotIndex { get; } = SlotIndex < GameSlotInfo.SlotCount
        ? SlotIndex
        : throw new ArgumentOutOfRangeException(nameof(SlotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");

    internal static GameSlotPlayInfo ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var x = reader.ReadInt32();
        var y = reader.ReadInt32();
        var isAirborne = reader.ReadBoolean();
        var hp = reader.ReadUInt32();
        var maxHp = reader.ReadUInt32();
        var seed = reader.ReadUInt32();
        var weaponId = reader.ReadUInt16();
        var maxAmmo = reader.ReadUInt16();
        var field3C = reader.ReadUInt16();
        var startingAmmo = reader.ReadUInt16();
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return new GameSlotPlayInfo(slotIndex, x, y, isAirborne, hp, maxHp, seed, weaponId, maxAmmo, field3C, startingAmmo);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteInt32(X);
        writer.WriteInt32(Y);
        writer.WriteBoolean(IsAirborne);
        writer.WriteUInt32(Hp);
        writer.WriteUInt32(MaxHp);
        writer.WriteUInt32(Seed);
        writer.WriteUInt16(WeaponId);
        writer.WriteUInt16(MaxAmmo);
        writer.WriteUInt16(Field3C);
        writer.WriteUInt16(StartingAmmo);
    }
}
