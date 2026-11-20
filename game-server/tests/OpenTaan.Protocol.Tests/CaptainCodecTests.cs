using OpenTaan.Protocol;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// Choosing a room's captains: the host asking, players volunteering and withdrawing, and
/// the room told. Every value here is invented, and every expected body is written out by
/// hand from the client's layout.
/// </summary>
public sealed class CaptainCodecTests
{
    [Fact]
    public void AskingForTheCaptainsAndBeingToldAreEachTheirIdAlone()
    {
        byte[] request = [.. Encoded.UInt32(PqGameSessionChiefDecision.RegistryId)];
        byte[] told = [.. Encoded.UInt32(PcGameSessionChiefDecision.RegistryId)];

        Assert.Equal(request, Encoded.Bytes(new PqGameSessionChiefDecision().Write));
        Assert.True(PqGameSessionChiefDecision.TryRead(request).Succeeded);
        Assert.Equal(told, Encoded.Bytes(new PcGameSessionChiefDecision().Write));
        Assert.True(PcGameSessionChiefDecision.TryRead(told).Succeeded);
    }

    [Fact]
    public void AskingForTheCaptainsWithAnythingAfterTheIdIsRefused()
    {
        byte[] body = [.. Encoded.UInt32(PqGameSessionChiefDecision.RegistryId), 0x01];

        Assert.Equal(PacketError.TrailingBytes, PqGameSessionChiefDecision.TryRead(body).Error);
    }

    [Fact]
    public void VolunteeringWithdrawingAndBeingRefusedAreEachTheCharacter()
    {
        byte[] volunteer = [.. Encoded.UInt32(PqGameSessionChallengeChief.RegistryId), .. Encoded.UInt32(0x01020304)];
        byte[] volunteered = [.. Encoded.UInt32(PcGameSessionChallengeChief.RegistryId), .. Encoded.UInt32(0x05060708)];
        byte[] withdraw = [.. Encoded.UInt32(PqGameSessionCancelChief.RegistryId), .. Encoded.UInt32(0x090A0B0C)];
        byte[] withdrawn = [.. Encoded.UInt32(PcGameSessionCancelChief.RegistryId), .. Encoded.UInt32(0x0D0E0F10)];
        byte[] refused = [.. Encoded.UInt32(PcGameSessionFailChallengeChief.RegistryId), .. Encoded.UInt32(0x11121314)];

        Assert.Equal(volunteer, Encoded.Bytes(new PqGameSessionChallengeChief(0x01020304).Write));
        Assert.Equal(new PqGameSessionChallengeChief(0x01020304), PqGameSessionChallengeChief.TryRead(volunteer).Packet);
        Assert.Equal(volunteered, Encoded.Bytes(new PcGameSessionChallengeChief(0x05060708).Write));
        Assert.Equal(new PcGameSessionChallengeChief(0x05060708), PcGameSessionChallengeChief.TryRead(volunteered).Packet);
        Assert.Equal(withdraw, Encoded.Bytes(new PqGameSessionCancelChief(0x090A0B0C).Write));
        Assert.Equal(new PqGameSessionCancelChief(0x090A0B0C), PqGameSessionCancelChief.TryRead(withdraw).Packet);
        Assert.Equal(withdrawn, Encoded.Bytes(new PcGameSessionCancelChief(0x0D0E0F10).Write));
        Assert.Equal(new PcGameSessionCancelChief(0x0D0E0F10), PcGameSessionCancelChief.TryRead(withdrawn).Packet);
        Assert.Equal(refused, Encoded.Bytes(new PcGameSessionFailChallengeChief(0x11121314).Write));
        Assert.Equal(new PcGameSessionFailChallengeChief(0x11121314), PcGameSessionFailChallengeChief.TryRead(refused).Packet);
    }

    [Fact]
    public void AVolunteeringOrWithdrawalCutShortOrWithBytesLeftOverIsRefused()
    {
        byte[] volunteer = [.. Encoded.UInt32(PqGameSessionChallengeChief.RegistryId), .. Encoded.UInt32(0x01020304)];
        byte[] withdraw = [.. Encoded.UInt32(PqGameSessionCancelChief.RegistryId), .. Encoded.UInt32(0x01020304)];

        Assert.Equal(PacketError.Truncated, PqGameSessionChallengeChief.TryRead(volunteer[..^1]).Error);
        Assert.Equal(PacketError.TrailingBytes, PqGameSessionChallengeChief.TryRead([.. volunteer, 0x00]).Error);
        Assert.Equal(PacketError.Truncated, PqGameSessionCancelChief.TryRead(withdraw[..^1]).Error);
        Assert.Equal(PacketError.TrailingBytes, PqGameSessionCancelChief.TryRead([.. withdraw, 0x00]).Error);
    }
}
