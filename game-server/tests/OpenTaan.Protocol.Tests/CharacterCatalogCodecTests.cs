using OpenTaan.Protocol;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Items;
using static OpenTaan.Tests.Protocol.CharacterSamples;

namespace OpenTaan.Tests.Protocol;

/// <summary>The objects the character packets carry: characters, appearances, catalogs and inventories.</summary>
public sealed class CharacterCatalogCodecTests
{
    private static readonly byte[] NewCharacterHeader = [.. Encoded.UInt32(PrNewCharacter.RegistryId), 0x00, 0x01, 0x01];

    [Fact]
    public void ACatalogListsEachCharacterThenEachAppearanceInTheClientsLayout()
    {
        var reply = new PrNewCharacter(NewCharacterStatus.Created, 0x01, new CharacterCatalog([Character], [Appearance]));
        byte[] expected = [.. NewCharacterHeader, .. CatalogBytes];

        Assert.Equal(expected, Encoded.Bytes(reply.Write));

        var read = PrNewCharacter.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(Character, Assert.Single(read.Packet.Catalog!.Characters));

        var appearance = Assert.Single(read.Packet.Catalog.Appearances);
        Assert.Equal((Appearance.Gender, Appearance.Field08, Appearance.Field0C), (appearance.Gender, appearance.Field08, appearance.Field0C));
        Assert.Equal(Slots, appearance.Slots);
    }

    [Theory]
    [InlineData(AppearanceSlot.Count - 1)]
    [InlineData(AppearanceSlot.Count + 1)]
    public void AnAppearanceWithoutExactlySixteenPartsCannotBeBuilt(int count)
    {
        var slots = new AppearanceSlot[count];

        Assert.Throws<ArgumentException>(() => new CharacterAppear(Appearance.Gender, 0, 0, slots));
    }

    [Fact]
    public void ACatalogWithoutOneAppearancePerCharacterCannotBeBuilt()
    {
        Assert.Throws<ArgumentException>(() => new CharacterCatalog([Character], []));
        Assert.Throws<ArgumentException>(() => new CharacterCatalog([], [Appearance]));
    }

    [Fact]
    public void AReceivedCatalogWithoutOneAppearancePerCharacterIsRefused()
    {
        // The client would read past the end of its appearance list for the character
        // that has none.
        byte[] body =
        [
            .. NewCharacterHeader,
            .. Encoded.UInt32(CharacterCatalog.RegistryId),
            .. Encoded.UInt32(1),
            .. CharacterBytes,
            .. Encoded.UInt32(0),
        ];

        Assert.Equal(PacketError.UnexpectedValue, PrNewCharacter.TryRead(body).Error);
    }

    [Theory]
    [InlineData(0x01, PacketError.Unsupported)]
    [InlineData(0x02, PacketError.UnexpectedValue)]
    public void AnAppearanceCarryingAnythingAfterItsPartsIsRefused(byte flag, PacketError expected)
    {
        byte[] body = [.. NewCharacterHeader, .. CatalogBytes];
        body[^1] = flag;

        Assert.Equal(expected, PrNewCharacter.TryRead(body).Error);
    }

    [Fact]
    public void AnEmptyCatalogIsItsIdAndTwoEmptyLists()
    {
        var body = Encoded.Bytes(new PrDelCharacter(DeleteCharacterStatus.Deleted, 0x01, CharacterCatalog.Empty).Write);

        Assert.Equal(
            [.. Encoded.UInt32(PrDelCharacter.RegistryId), 0x00, 0x01, 0x01, .. Encoded.UInt32(CharacterCatalog.RegistryId), .. Encoded.UInt32(0), .. Encoded.UInt32(0)],
            body);
    }

    [Fact]
    public void AnInventoryIsItsTypeTwoBytesAndAnEmptyItemList()
    {
        var reply = new PrSelectCharacter([], [new Inventory(0x0A0B0C0D, 0x0E, 0x0F)], [], [], [], []);

        byte[] expected =
        [
            .. Encoded.UInt32(PrSelectCharacter.RegistryId),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(Inventory.RegistryId), .. Encoded.UInt32(0x0A0B0C0D), 0x0E, 0x0F, .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(0),
        ];

        Assert.Equal(expected, Encoded.Bytes(reply.Write));
        Assert.Equal(new Inventory(0x0A0B0C0D, 0x0E, 0x0F), Assert.Single(PrSelectCharacter.TryRead(expected).Packet!.ItemInventories));
    }

    [Fact]
    public void AnInventoryHoldingItemsIsRefusedUntilItemsAreSupported()
    {
        byte[] body =
        [
            .. Encoded.UInt32(PrSelectCharacter.RegistryId),
            .. Encoded.UInt32(0),
            .. Encoded.UInt32(1),
            .. Encoded.UInt32(Inventory.RegistryId), .. Encoded.UInt32(2), 0x00, 0x00, .. Encoded.UInt32(1),
            .. new byte[64],
        ];

        Assert.Equal(PacketError.Unsupported, PrSelectCharacter.TryRead(body).Error);
    }

    [Fact]
    public void ACharacterCountLargerThanTheBodyIsRefusedBeforeAnythingIsRead()
    {
        byte[] body = [.. Encoded.UInt32(PrSelectCharacter.RegistryId), .. Encoded.UInt32(uint.MaxValue), .. CharacterBytes];

        Assert.Equal(PacketError.CountOutOfRange, PrSelectCharacter.TryRead(body).Error);
    }
}
