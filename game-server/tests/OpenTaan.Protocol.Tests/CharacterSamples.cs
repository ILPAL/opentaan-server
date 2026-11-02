using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// A character and an appearance whose fields all differ, each with the bytes the
/// client's layout puts them in, written out by hand rather than by a codec.
/// </summary>
internal static class CharacterSamples
{
    public static readonly CharacterInfo Character = new(
        CharacterId: 0x0A0B0C0D,
        Name: "Kim",
        Level: 0x0102,
        CurrentExperience: 0x11111111,
        ExperienceFloor: 0x22222222,
        ExperienceCeiling: 0x33333333,
        Gold: 0x44444444,
        Gender: Gender.Female,
        RankPlacement: 0x55555555,
        RankScore: 0x66666666,
        LeagueMoney: 0x77777777,
        SoulPoints: 0x88888888,
        Wins: 0x99999999,
        Losses: 0xAAAAAAAA,
        ForceMedals: 0xBBBBBBBB,
        KillMedals: 0xCCCCCCCC,
        SupportMedals: 0xDDDDDDDD,
        GuildName: "Rồng");

    public static readonly byte[] CharacterBytes =
    [
        .. Encoded.UInt32(CharacterInfo.RegistryId),
        .. Encoded.UInt32(0x0A0B0C0D),
        .. Encoded.Utf16("Kim"),
        .. Encoded.UInt16(0x0102),
        .. Encoded.UInt32(0x11111111),
        .. Encoded.UInt32(0x22222222),
        .. Encoded.UInt32(0x33333333),
        .. Encoded.UInt32(0x44444444),
        0x02,
        .. Encoded.UInt32(0x55555555),
        .. Encoded.UInt32(0x66666666),
        .. Encoded.UInt32(0x77777777),
        .. Encoded.UInt32(0x88888888),
        .. Encoded.UInt32(0x99999999),
        .. Encoded.UInt32(0xAAAAAAAA),
        .. Encoded.UInt32(0xBBBBBBBB),
        .. Encoded.UInt32(0xCCCCCCCC),
        .. Encoded.UInt32(0xDDDDDDDD),
        .. Encoded.Utf16("Rồng"),
    ];

    /// <summary>Sixteen parts, each field of each part different from every other.</summary>
    public static readonly AppearanceSlot[] Slots =
    [
        .. Enumerable.Range(0, AppearanceSlot.Count).Select(index => new AppearanceSlot(
            (uint)(0x10000 + index),
            (ushort)(0x2000 + index),
            (ushort)(0x3000 + index),
            (ushort)(0x4000 + index))),
    ];

    public static readonly byte[] SlotBytes =
    [
        .. Enumerable.Range(0, AppearanceSlot.Count).SelectMany(index => (byte[])
        [
            .. Encoded.UInt32((uint)(0x10000 + index)),
            .. Encoded.UInt16((ushort)(0x2000 + index)),
            .. Encoded.UInt16((ushort)(0x3000 + index)),
            .. Encoded.UInt16((ushort)(0x4000 + index)),
        ]),
    ];

    public static readonly CharacterAppear Appearance = new(Gender.Female, 0x01020304, 0x0506, Slots);

    public static readonly byte[] AppearanceBytes =
    [
        .. Encoded.UInt32(CharacterAppear.RegistryId),
        0x02,
        .. Encoded.UInt32(0x01020304),
        .. Encoded.UInt16(0x0506),
        .. SlotBytes,
        0x00,
    ];

    /// <summary>A catalog holding <see cref="Character"/> and <see cref="Appearance"/>, as bytes.</summary>
    public static readonly byte[] CatalogBytes =
    [
        .. Encoded.UInt32(CharacterCatalog.RegistryId),
        .. Encoded.UInt32(1),
        .. CharacterBytes,
        .. Encoded.UInt32(1),
        .. AppearanceBytes,
    ];
}
