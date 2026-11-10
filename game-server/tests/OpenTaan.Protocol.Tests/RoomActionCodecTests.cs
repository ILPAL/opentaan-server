using OpenTaan.Protocol;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// What players do in a room: being ready, changing team and weapon, the host's options,
/// slots and expulsions, asked for and told. Every value here is invented, each field's
/// bytes unlike its neighbours', and every expected body is written out by hand from the
/// client's layout.
/// </summary>
public sealed class RoomActionCodecTests
{
    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void AReadyRequestIsOneFlag(bool ready, byte expectedByte)
    {
        var request = new PqGameSessionReady(ready);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionReady.RegistryId), expectedByte];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionReady.TryRead(expected).Packet);
    }

    [Fact]
    public void AReadyFlagOtherThanZeroOrOneIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqGameSessionReady.RegistryId), 0x02];

        Assert.Equal(PacketError.UnexpectedValue, PqGameSessionReady.TryRead(body).Error);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x07)]
    public void ATeamRequestIsTheTeamAskedForWhateverItIs(byte team)
    {
        var request = new PqGameSessionChangeTeam(team);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionChangeTeam.RegistryId), team];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionChangeTeam.TryRead(expected).Packet);
    }

    [Fact]
    public void AWeaponRequestIsTheCharacterThenTheWeapon()
    {
        var request = new PqGameSessionChangeWeapon(0x01020304, 0x0506);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionChangeWeapon.RegistryId), .. Encoded.UInt32(0x01020304), .. Encoded.UInt16(0x0506)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionChangeWeapon.TryRead(expected).Packet);
    }

    [Fact]
    public void AnOptionsRequestIsTheMapTheModeTheChancesThenTheDifficulty()
    {
        var request = new PqGameSessionChangeGameOption(0x0A0B, GameMode.CaptainNoItems, ChanceMode.Bonus, 0x01);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionChangeGameOption.RegistryId), .. Encoded.UInt16(0x0A0B), .. Encoded.UInt16(0x0003), 0x02, 0x01];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionChangeGameOption.TryRead(expected).Packet);
    }

    [Fact]
    public void AnOptionsRequestIsReadWhateverValuesItCarriesSoThatItCanBeAnswered()
    {
        // A mode and chances the client never sends, and the difficulty the dialog sends
        // outside a mission room.
        byte[] body = [.. Encoded.UInt32(PqGameSessionChangeGameOption.RegistryId), .. Encoded.UInt16(0xFFFF), .. Encoded.UInt16(0x0909), 0x07, 0xFF];

        var read = PqGameSessionChangeGameOption.TryRead(body);

        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(new PqGameSessionChangeGameOption(0xFFFF, (GameMode)0x0909, (ChanceMode)0x07, 0xFF), read.Packet);
    }

    [Theory]
    [InlineData(true, 0x01, 0x05)]
    [InlineData(false, 0x00, 0x05)]
    [InlineData(false, 0x00, 0xFE)]
    public void ASlotRequestIsTheFlagThenWhicheverSlotTheClientNames(bool open, byte flag, byte slot)
    {
        var request = new PqGameSessionOpenCloseSlot(open, slot);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionOpenCloseSlot.RegistryId), flag, slot];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionOpenCloseSlot.TryRead(expected).Packet);
    }

    [Fact]
    public void ASlotRequestsFlagOtherThanZeroOrOneIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqGameSessionOpenCloseSlot.RegistryId), 0x02, 0x05];

        Assert.Equal(PacketError.UnexpectedValue, PqGameSessionOpenCloseSlot.TryRead(body).Error);
    }

    [Theory]
    [InlineData(0x01020304u)]
    [InlineData(PqGameSessionUserExpel.NoCharacter)]
    public void AnExpelRequestIsTheCharacterInTheSlotClicked(uint characterId)
    {
        var request = new PqGameSessionUserExpel(characterId);

        byte[] expected = [.. Encoded.UInt32(PqGameSessionUserExpel.RegistryId), .. Encoded.UInt32(characterId)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGameSessionUserExpel.TryRead(expected).Packet);
    }

    [Fact]
    public void ARequestCutShortOrWithBytesLeftOverIsRefused()
    {
        byte[] weapon = [.. Encoded.UInt32(PqGameSessionChangeWeapon.RegistryId), .. Encoded.UInt32(0x01020304), .. Encoded.UInt16(0x0506)];

        Assert.Equal(PacketError.Truncated, PqGameSessionChangeWeapon.TryRead(weapon[..^1]).Error);
        Assert.Equal(PacketError.TrailingBytes, PqGameSessionChangeWeapon.TryRead([.. weapon, 0x00]).Error);
    }

    [Fact]
    public void ATeamChangeIsThePlayerTheTeamThenBothSlotsLeftThenBothSlotsTaken()
    {
        var change = new PcGameSessionChangeTeam(0x01020304, 2, 0x00, 0x01, 0x06, 0x07);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionChangeTeam.RegistryId), .. Encoded.UInt32(0x01020304), 0x02, 0x00, 0x01, 0x06, 0x07];

        Assert.Equal(expected, Encoded.Bytes(change.Write));
        Assert.Equal(change, PcGameSessionChangeTeam.TryRead(expected).Packet);
    }

    [Fact]
    public void AOneCharacterTeamChangeCarriesNoSlotForTheSecondMove()
    {
        var change = PcGameSessionChangeTeam.Moving(0x01020304, 1, [0x06], [0x05]);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionChangeTeam.RegistryId), .. Encoded.UInt32(0x01020304), 0x01, 0x06, 0xFF, 0x05, 0xFF];

        Assert.Equal(new PcGameSessionChangeTeam(0x01020304, 1, 0x06, PcGameSessionChangeTeam.NoSlot, 0x05, PcGameSessionChangeTeam.NoSlot), change);
        Assert.Equal(expected, Encoded.Bytes(change.Write));
        Assert.Equal(change, PcGameSessionChangeTeam.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(0x00, 0x00, 0xFF, 0x04, 0xFF)] // no team
    [InlineData(0x03, 0x00, 0xFF, 0x04, 0xFF)] // a third team
    [InlineData(0x01, 0x08, 0xFF, 0x04, 0xFF)] // a first slot outside the room
    [InlineData(0x01, 0x00, 0xFF, 0x08, 0xFF)] // a first slot taken outside the room
    [InlineData(0x01, 0xFF, 0x00, 0xFF, 0x04)] // no first move
    [InlineData(0x01, 0x00, 0x01, 0x04, 0xFF)] // half a second move
    [InlineData(0x01, 0x00, 0xFF, 0x04, 0x05)] // the other half
    [InlineData(0x01, 0x00, 0x09, 0x04, 0x05)] // a second slot outside the room
    public void ATeamChangeTheClientWouldMisplaceCanBeNeitherReadNorBuilt(byte team, byte firstFrom, byte secondFrom, byte firstTo, byte secondTo)
    {
        byte[] body = [.. Encoded.UInt32(PcGameSessionChangeTeam.RegistryId), .. Encoded.UInt32(0x01020304), team, firstFrom, secondFrom, firstTo, secondTo];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionChangeTeam.TryRead(body).Error);
        Assert.ThrowsAny<ArgumentException>(() => new PcGameSessionChangeTeam(0x01020304, team, firstFrom, secondFrom, firstTo, secondTo));
    }

    [Fact]
    public void ATeamChangeMovesOneOrTwoCharactersEachFromASlotToASlot()
    {
        Assert.Throws<ArgumentException>(() => PcGameSessionChangeTeam.Moving(1, 1, [], []));
        Assert.Throws<ArgumentException>(() => PcGameSessionChangeTeam.Moving(1, 1, [2, 3], [1]));
        Assert.Throws<ArgumentException>(() => PcGameSessionChangeTeam.Moving(1, 1, [2, 3, 6], [0, 1, 4]));
    }

    [Fact]
    public void AWeaponChangeIsTheCharacterThenTheWeapon()
    {
        var change = new PcGameSessionChangeWeapon(0x01020304, 0x0506);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionChangeWeapon.RegistryId), .. Encoded.UInt32(0x01020304), .. Encoded.UInt16(0x0506)];

        Assert.Equal(expected, Encoded.Bytes(change.Write));
        Assert.Equal(change, PcGameSessionChangeWeapon.TryRead(expected).Packet);
    }

    [Fact]
    public void ARoomsOptionsAreTheMapTheModeTheChancesThenTheDifficulty()
    {
        var options = new PcGameSessionChangeGameOption(0x0A0B, GameMode.CaptainItems, ChanceMode.Card, 0x03);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionChangeGameOption.RegistryId), .. Encoded.UInt16(0x0A0B), .. Encoded.UInt16(0x0002), 0x01, 0x03];

        Assert.Equal(expected, Encoded.Bytes(options.Write));
        Assert.Equal(options, PcGameSessionChangeGameOption.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void ASlotOpenedOrClosedIsTheFlagThenTheSlot(bool open, byte flag)
    {
        var change = new PcGameSessionOpenCloseSlot(open, 0x07);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionOpenCloseSlot.RegistryId), flag, 0x07];

        Assert.Equal(expected, Encoded.Bytes(change.Write));
        Assert.Equal(change, PcGameSessionOpenCloseSlot.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(0x01, 0x08)]
    [InlineData(0x00, 0xFF)]
    [InlineData(0x02, 0x03)]
    public void ASlotOutsideTheRoomOrAFlagOtherThanZeroOrOneIsRefused(byte flag, byte slot)
    {
        byte[] body = [.. Encoded.UInt32(PcGameSessionOpenCloseSlot.RegistryId), flag, slot];

        Assert.Equal(PacketError.UnexpectedValue, PcGameSessionOpenCloseSlot.TryRead(body).Error);
    }

    [Fact]
    public void ASlotOutsideTheRoomCannotBeOpenedOrClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGameSessionOpenCloseSlot(true, GameSlotInfo.SlotCount));
    }

    [Fact]
    public void AnExpulsionIsTwoUnusedStringsThenThePlayersId()
    {
        var expulsion = new PcGameSessionUserExpel("một", "hai", 0x01020304);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionUserExpel.RegistryId), .. Encoded.Utf16("một"), .. Encoded.Utf16("hai"), .. Encoded.UInt32(0x01020304)];

        Assert.Equal(expected, Encoded.Bytes(expulsion.Write));
        Assert.Equal(expulsion, PcGameSessionUserExpel.TryRead(expected).Packet);
    }

    [Fact]
    public void AnExpulsionThisServerSendsLeavesTheUnusedStringsEmpty()
    {
        var expulsion = new PcGameSessionUserExpel(0x01020304);

        byte[] expected = [.. Encoded.UInt32(PcGameSessionUserExpel.RegistryId), .. Encoded.Utf16(string.Empty), .. Encoded.Utf16(string.Empty), .. Encoded.UInt32(0x01020304)];

        Assert.Equal(expected, Encoded.Bytes(expulsion.Write));
        Assert.Equal(new PcGameSessionUserExpel(string.Empty, string.Empty, 0x01020304), expulsion);
    }
}
