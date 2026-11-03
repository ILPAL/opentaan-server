using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Data;
using OpenTaan.Protocol.Lobby;

namespace OpenTaan.Tests.Protocol;

/// <summary>Cash, the channel list, channel moves and the lobby. Every value here is invented.</summary>
public sealed class ChannelAndLobbyCodecTests
{
    private static readonly ChannelInfo Channel = new(
        Name: "Kênh 1",
        ChannelId: 0x0102,
        Mode: 0x06,
        ServerAddress: new ConnectionAddress(0x0A0B0C0D, 0x0E0F),
        FillPercent: 0x11111111,
        Field1C: 0x22222222,
        MinimumLevel: 0x3333,
        MaximumLevel: 0x4444,
        Disabled: 0x01);

    private static readonly byte[] ChannelBytes =
    [
        .. Encoded.UInt32(ChannelInfo.RegistryId),
        .. Encoded.Utf16("Kênh 1"),
        .. Encoded.UInt16(0x0102),
        0x06,
        .. Encoded.UInt32(0x0A0B0C0D), .. Encoded.UInt16(0x0E0F),
        .. Encoded.UInt32(0x11111111),
        .. Encoded.UInt32(0x22222222),
        .. Encoded.UInt16(0x3333),
        .. Encoded.UInt16(0x4444),
        0x01,
    ];

    [Fact]
    public void ACashPacketIsTheCashThenTheSlotBothFullWidth()
    {
        var packet = new PcUpdateCash(0x0A0B0C0D, 0x01020304);

        byte[] expected = [.. Encoded.UInt32(PcUpdateCash.RegistryId), .. Encoded.UInt32(0x0A0B0C0D), .. Encoded.UInt32(0x01020304)];

        Assert.Equal(expected, Encoded.Bytes(packet.Write));
        Assert.Equal(packet, PcUpdateCash.TryRead(expected).Packet);
    }

    [Fact]
    public void AChannelListRequestIsItsIdAlone()
    {
        byte[] expected = Encoded.UInt32(PqGetChannelList.RegistryId);

        Assert.Equal(expected, Encoded.Bytes(new PqGetChannelList().Write));
        Assert.True(PqGetChannelList.TryRead(expected).Succeeded);
        Assert.Equal(PacketError.TrailingBytes, PqGetChannelList.TryRead([.. expected, 0x00]).Error);
    }

    [Fact]
    public void TheChannelListIsATreeInAPlainEnvelopePrecededByItsLength()
    {
        var tree = DataNode.Element(string.Empty, [], [DataNode.Element("server", [new NamedValue("name", "North")], [])]);
        var envelope = DataEnvelope.Wrap(tree.ToBytes());

        byte[] expected = [.. Encoded.UInt32(PrGetChannelList.RegistryId), .. Encoded.UInt32((uint)envelope.Length), .. envelope];

        Assert.Equal(expected, Encoded.Bytes(new PrGetChannelList(tree).Write));
        Assert.Equal(0x00, envelope[1]);

        var read = PrGetChannelList.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal("North", Assert.Single(read.Packet.Channels.Children).Attribute("name"));
    }

    [Fact]
    public void AChannelListWhoseEnvelopeFailsItsChecksumIsRefused()
    {
        var body = Encoded.Bytes(new PrGetChannelList(DataNode.Element("server", [], [])).Write);
        body[^1] ^= 0xFF;

        Assert.Equal(PacketError.UnexpectedValue, PrGetChannelList.TryRead(body).Error);
    }

    [Fact]
    public void AChannelMoveRequestIsTheFlagTheChannelTheContextAndTheVersion()
    {
        var request = new PqChannelMoveTo(true, 0x0102, 0x0A0B0C0D, 0x11223344);

        byte[] expected = [.. Encoded.UInt32(PqChannelMoveTo.RegistryId), 0x01, .. Encoded.UInt16(0x0102), .. Encoded.UInt32(0x0A0B0C0D), .. Encoded.UInt32(0x11223344)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqChannelMoveTo.TryRead(expected).Packet);
    }

    [Fact]
    public void AnEnteredMoveCarriesTheChannelAfterItsPresenceFlag()
    {
        var answer = new PrChannelMoveTo(
            ChannelMoveResult.Entered,
            0x0102,
            new ConnectionAddress(0x0A0B0C0D, 0x0E0F),
            [new MovingCharacter(0x11111111, 0x22222222)],
            Channel);

        byte[] expected =
        [
            .. Encoded.UInt32(PrChannelMoveTo.RegistryId),
            0x00,
            .. Encoded.UInt16(0x0102),
            .. Encoded.UInt32(0x0A0B0C0D), .. Encoded.UInt16(0x0E0F),
            .. Encoded.UInt32(1), .. Encoded.UInt32(0x11111111), .. Encoded.UInt32(0x22222222),
            0x01, .. ChannelBytes,
        ];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));

        var read = PrChannelMoveTo.TryRead(expected);
        Assert.True(read.Succeeded, read.Error.ToString());
        Assert.Equal(Channel, read.Packet.Channel);
        Assert.Equal(answer.Characters, read.Packet.Characters);
    }

    [Fact]
    public void ARefusedMoveWithoutAChannelStillCarriesTheEmptyPresenceFlag()
    {
        var answer = new PrChannelMoveTo(ChannelMoveResult.ChannelFull, 0x0102, new ConnectionAddress(0, 0), [], Channel: null);

        byte[] expected = [.. Encoded.UInt32(PrChannelMoveTo.RegistryId), 100, .. Encoded.UInt16(0x0102), .. Encoded.UInt32(0), .. Encoded.UInt16(0), .. Encoded.UInt32(0), 0x00];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Null(PrChannelMoveTo.TryRead(expected).Packet?.Channel);
    }

    [Fact]
    public void AMoveResultTheClientIgnoresCannotBeWritten()
    {
        // The client does nothing for these and never leaves its channel screen.
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrChannelMoveTo((ChannelMoveResult)2, 1, new ConnectionAddress(0, 0), [], null).Write));
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrChannelMoveTo((ChannelMoveResult)99, 1, new ConnectionAddress(0, 0), [], null).Write));
    }

    [Fact]
    public void ALobbyEntryIsTheMoveFlagTheTeamModeAndTheTeamKey()
    {
        var request = new PqLobbyEnter(0x01, 0xFF, 0x0A0B0C0D);

        byte[] expected = [.. Encoded.UInt32(PqLobbyEnter.RegistryId), 0x01, 0xFF, .. Encoded.UInt32(0x0A0B0C0D)];

        Assert.Equal(expected, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqLobbyEnter.TryRead(expected).Packet);
    }

    [Theory]
    [InlineData(LobbyEnterResult.Entered, 0x00)]
    [InlineData(LobbyEnterResult.Failed, 0x04)]
    public void ALobbyAnswerIsTheChannelThenTheResult(LobbyEnterResult result, byte expectedResult)
    {
        var answer = new PrLobbyEnter(0x0A0B0C0D, result);

        byte[] expected = [.. Encoded.UInt32(PrLobbyEnter.RegistryId), .. Encoded.UInt32(0x0A0B0C0D), expectedResult];

        Assert.Equal(expected, Encoded.Bytes(answer.Write));
        Assert.Equal(answer, PrLobbyEnter.TryRead(expected).Packet);
        Assert.Throws<InvalidOperationException>(() => Encoded.Bytes(new PrLobbyEnter(1, (LobbyEnterResult)5).Write));
    }

    [Fact]
    public void AnEmptyRoomListIsItsIdAndAZeroCount()
    {
        byte[] expected = [.. Encoded.UInt32(PcLobbyInitRoomList.RegistryId), .. Encoded.UInt32(0)];

        Assert.Equal(expected, Encoded.Bytes(new PcLobbyInitRoomList().Write));
        Assert.Equal(PacketError.Unsupported, PcLobbyInitRoomList.TryRead([.. Encoded.UInt32(PcLobbyInitRoomList.RegistryId), .. Encoded.UInt32(1), .. new byte[32]]).Error);
    }

    [Fact]
    public void ALobbyUserListPairsEachCharacterWithItsAppearance()
    {
        var list = new PcLobbyInitUserList([CharacterSamples.Character], [CharacterSamples.Appearance]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcLobbyInitUserList.RegistryId),
            .. Encoded.UInt32(1), .. CharacterSamples.CharacterBytes,
            .. Encoded.UInt32(1), .. CharacterSamples.AppearanceBytes,
        ];

        Assert.Equal(expected, Encoded.Bytes(list.Write));
        Assert.Equal(CharacterSamples.Character, Assert.Single(PcLobbyInitUserList.TryRead(expected).Packet!.Characters));

        Assert.Throws<ArgumentException>(() => new PcLobbyInitUserList([CharacterSamples.Character], []));
        byte[] unpaired = [.. Encoded.UInt32(PcLobbyInitUserList.RegistryId), .. Encoded.UInt32(1), .. CharacterSamples.CharacterBytes, .. Encoded.UInt32(0)];
        Assert.Equal(PacketError.UnexpectedValue, PcLobbyInitUserList.TryRead(unpaired).Error);
    }

    [Fact]
    public void LeavingTheLobbyIsTheIdAloneBothWays()
    {
        Assert.Equal(Encoded.UInt32(PqLobbyLeave.RegistryId), Encoded.Bytes(new PqLobbyLeave().Write));
        Assert.Equal(Encoded.UInt32(PrLobbyLeave.RegistryId), Encoded.Bytes(new PrLobbyLeave().Write));
        Assert.True(PqLobbyLeave.TryRead(Encoded.UInt32(PqLobbyLeave.RegistryId)).Succeeded);
        Assert.True(PrLobbyLeave.TryRead(Encoded.UInt32(PrLobbyLeave.RegistryId)).Succeeded);
    }

    [Fact]
    public void APortraitIsTheCharacterThenAPresenceFlagThenTheCountedImage()
    {
        var packet = new PcUploadWebImage(0x0A0B0C0D, [0x89, 0x50, 0x4E]);

        byte[] expected =
        [
            .. Encoded.UInt32(PcUploadWebImage.RegistryId),
            .. Encoded.UInt32(0x0A0B0C0D),
            0x01,
            .. Encoded.UInt32(3), 0x89, 0x50, 0x4E,
        ];

        Assert.Equal(expected, Encoded.Bytes(packet.Write));
        var read = PcUploadWebImage.TryRead(expected).Packet!;
        Assert.Equal(0x0A0B0C0Du, read.CharacterId);
        Assert.Equal([0x89, 0x50, 0x4E], read.Image);
    }

    [Fact]
    public void APortraitWithoutAnImageEndsAtItsPresenceFlag()
    {
        byte[] expected = [.. Encoded.UInt32(PcUploadWebImage.RegistryId), .. Encoded.UInt32(0x0A0B0C0D), 0x00];

        Assert.Equal(expected, Encoded.Bytes(new PcUploadWebImage(0x0A0B0C0D, Image: null).Write));
        Assert.Null(PcUploadWebImage.TryRead(expected).Packet!.Image);
    }

    [Fact]
    public void APortraitLongerThanItsBodyIsRefusedBeforeAnythingIsCopied()
    {
        byte[] body = [.. Encoded.UInt32(PcUploadWebImage.RegistryId), .. Encoded.UInt32(1), 0x01, .. Encoded.UInt32(0x7FFFFFFF), 0x00];

        Assert.Equal(PacketError.CountOutOfRange, PcUploadWebImage.TryRead(body).Error);
    }
}
