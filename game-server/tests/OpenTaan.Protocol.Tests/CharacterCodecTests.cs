using OpenTaan.Protocol;
using OpenTaan.Protocol.Items;
using OpenTaan.Protocol.Characters;

namespace OpenTaan.Tests.Protocol;

public sealed class CharacterCodecTests
{
    [Fact]
    public void AnExistenceRequestCarriesTheNameAsUtf16()
    {
        var request = new PqExistCharacter("Nam");

        byte[] expected = [.. Encoded.UInt32(PqExistCharacter.RegistryId), 0x03, 0x00, 0x00, 0x00, 0x4E, 0x00, 0x61, 0x00, 0x6D, 0x00];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqExistCharacter.TryRead(expected).Packet);
    }

    [Fact]
    public void AnExistenceRequestMayNameNothing()
    {
        var result = PqExistCharacter.TryRead(Encoded.Bytes(new PqExistCharacter(string.Empty).Write));

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Packet.CharacterName);
    }

    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void AnExistenceAnswerIsOneByteThatIsZeroOrOne(bool exists, byte expected)
    {
        var body = Encoded.Bytes(new PrExistCharacter(exists).Write);

        Assert.Equal([.. Encoded.UInt32(PrExistCharacter.RegistryId), expected], body);
        Assert.Equal(exists, PrExistCharacter.TryRead(body).Packet?.Exists);
    }

    [Fact]
    public void ANewCharacterRequestCarriesTheSlotTheNameAndSixteenParts()
    {
        var request = new PqNewCharacter(0x01, "Kim", CharacterSamples.Slots);

        byte[] expected = [.. Encoded.UInt32(PqNewCharacter.RegistryId), 0x01, .. Encoded.Utf16("Kim"), .. CharacterSamples.SlotBytes];

        Assert.Equal(expected, Encoded.Bytes(request.Write));

        var read = PqNewCharacter.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal((request.AccountSlot, request.Name), (read.Packet.AccountSlot, read.Packet.Name));
        Assert.Equal(CharacterSamples.Slots, read.Packet.Appearance);
    }

    [Fact]
    public void ANewCharacterRequestMissingPartOfItsAppearanceIsRefused()
    {
        var body = Encoded.Bytes(new PqNewCharacter(0x01, "Kim", CharacterSamples.Slots).Write);

        Assert.Equal(PacketError.Truncated, PqNewCharacter.TryRead(body.AsSpan(0, body.Length - 1)).Error);
    }

    [Fact]
    public void ANewCharacterRequestWithoutSixteenPartsCannotBeBuilt()
    {
        Assert.Throws<ArgumentException>(() => new PqNewCharacter(0x01, "Kim", CharacterSamples.Slots[..^1]));
    }

    [Fact]
    public void ANewCharacterAnswerWithoutACatalogStillCarriesTheEmptyPresenceFlag()
    {
        var answer = new PrNewCharacter(NewCharacterStatus.NameTaken, 0x01, Catalog: null);

        byte[] expected = [.. Encoded.UInt32(PrNewCharacter.RegistryId), 0x01, 0x01, 0x00];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrNewCharacter.TryRead(expected).Packet);
    }

    [Fact]
    public void ADeletionAnswerWithoutACatalogStillCarriesTheEmptyPresenceFlag()
    {
        var answer = new PrDelCharacter(DeleteCharacterStatus.InGuildAndHasSoulMate, 0x01, Catalog: null);

        byte[] expected = [.. Encoded.UInt32(PrDelCharacter.RegistryId), 0x04, 0x01, 0x00];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrDelCharacter.TryRead(expected).Packet);
    }

    [Fact]
    public void APresenceFlagOtherThanZeroOrOneIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PrDelCharacter.RegistryId), 0x00, 0x01, 0x02];

        Assert.Equal(PacketError.UnexpectedValue, PrDelCharacter.TryRead(body).Error);
    }

    [Fact]
    public void AStatusTheClientHasNoDialogForCannotBeWritten()
    {
        // The client shows nothing for these and waits for good.
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrNewCharacter((NewCharacterStatus)3, 0, null).Write));
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrDelCharacter((DeleteCharacterStatus)5, 0, null).Write));
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrConfirmName((ConfirmNameStatus)2).Write));
    }

    [Fact]
    public void ASelectionRequestCarriesTheIdsAsACountedList()
    {
        var request = new PqSelectCharacter([0x0A0B0C0D, 0x01020304]);

        byte[] expected = [.. Encoded.UInt32(PqSelectCharacter.RegistryId), .. Encoded.UInt32(2), .. Encoded.UInt32(0x0A0B0C0D), .. Encoded.UInt32(0x01020304)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request.CharacterIds, PqSelectCharacter.TryRead(expected).Packet?.CharacterIds);
    }

    [Fact]
    public void ASelectionRequestCountingMoreIdsThanItHoldsIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqSelectCharacter.RegistryId), .. Encoded.UInt32(3), .. Encoded.UInt32(0x0A0B0C0D)];

        Assert.Equal(PacketError.CountOutOfRange, PqSelectCharacter.TryRead(body).Error);
    }

    [Fact]
    public void ASelectionAnswerListsCharactersThenItemEquipmentAndBingoInventoriesThenFlags()
    {
        var answer = new PrSelectCharacter(
            Characters: [CharacterSamples.Character],
            ItemInventories: [new Inventory(0x0A, 0x1A, 0x2A)],
            EquipmentInventories: [new Inventory(0x0B, 0x1B, 0x2B)],
            BingoInventories: [new Inventory(0x0C, 0x1C, 0x2C)],
            TutorialFlags: [0x05],
            SlotUnlockFlags: [0x06]);

        byte[] expected =
        [
            .. Encoded.UInt32(PrSelectCharacter.RegistryId),
            .. Encoded.UInt32(1), .. CharacterSamples.CharacterBytes,
            .. Encoded.UInt32(1), .. Encoded.UInt32(Inventory.RegistryId), .. Encoded.UInt32(0x0A), 0x1A, 0x2A, .. Encoded.UInt32(0),
            .. Encoded.UInt32(1), .. Encoded.UInt32(Inventory.RegistryId), .. Encoded.UInt32(0x0B), 0x1B, 0x2B, .. Encoded.UInt32(0),
            .. Encoded.UInt32(1), .. Encoded.UInt32(Inventory.RegistryId), .. Encoded.UInt32(0x0C), 0x1C, 0x2C, .. Encoded.UInt32(0),
            .. Encoded.UInt32(1), 0x05,
            .. Encoded.UInt32(1), 0x06,
        ];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));

        var read = PrSelectCharacter.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(answer.Characters, read.Packet.Characters);
        Assert.Equal(answer.ItemInventories, read.Packet.ItemInventories);
        Assert.Equal(answer.EquipmentInventories, read.Packet.EquipmentInventories);
        Assert.Equal(answer.BingoInventories, read.Packet.BingoInventories);
        Assert.Equal(answer.TutorialFlags, read.Packet.TutorialFlags);
        Assert.Equal(answer.SlotUnlockFlags, read.Packet.SlotUnlockFlags);
    }

    [Fact]
    public void ANameCheckCarriesTheSlotBeforeTheName()
    {
        var request = new PqConfirmName(0x01, "Rồng");

        byte[] expected = [.. Encoded.UInt32(PqConfirmName.RegistryId), 0x01, .. Encoded.Utf16("Rồng")];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqConfirmName.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(ConfirmNameStatus.Available, 0x00)]
    [InlineData(ConfirmNameStatus.Taken, 0x01)]
    public void ANameCheckAnswerIsOneByte(ConfirmNameStatus status, byte expected)
    {
        var body = Encoded.Bytes(new PrConfirmName(status).Write);

        Assert.Equal([.. Encoded.UInt32(PrConfirmName.RegistryId), expected], body);
        Assert.Equal(status, PrConfirmName.TryRead(body).Packet?.Status);
    }

    [Fact]
    public void ADeletionRequestCarriesTheSlotThenTheCharacterId()
    {
        var request = new PqDelCharacter(0x01, 0x0A0B0C0D);

        byte[] expected = [.. Encoded.UInt32(PqDelCharacter.RegistryId), 0x01, .. Encoded.UInt32(0x0A0B0C0D)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqDelCharacter.TryRead(expected).Packet);
    }
}
