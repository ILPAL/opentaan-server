using System.Net;
using OpenTaan.GameServer.Channels;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Tests.GameServer;

/// <summary>Choosing the address the client is told the channels are at.</summary>
public sealed class AdvertisedAddressTests
{
    private static readonly IPAddress Detected = IPAddress.Parse("192.168.10.20");

    [Fact]
    public void AConfiguredAddressComesFirst()
    {
        var chosen = AdvertisedAddress.Choose("203.0.113.7", "10.0.0.5", 2190, () => Detected);

        Assert.Equal(new AdvertisedAddress(IPAddress.Parse("203.0.113.7"), 2190, AddressSource.Configured), chosen);
    }

    [Fact]
    public void ABindAddressThatNamesOneComesNext()
    {
        var chosen = AdvertisedAddress.Choose(null, "10.0.0.5", 2190, () => Detected);

        Assert.Equal(new AdvertisedAddress(IPAddress.Parse("10.0.0.5"), 2190, AddressSource.BindAddress), chosen);
    }

    [Fact]
    public void BindingEveryInterfaceLeavesTheAddressToBeWorkedOut()
    {
        var chosen = AdvertisedAddress.Choose(" ", "0.0.0.0", 2190, () => Detected);

        Assert.Equal(new AdvertisedAddress(Detected, 2190, AddressSource.Detected), chosen);
    }

    [Fact]
    public void TheClientHoldsTheAddressBytesInNetworkOrder()
    {
        var wire = new AdvertisedAddress(IPAddress.Parse("203.0.113.7"), 2190, AddressSource.Configured).ToWire();

        // 203 = 0xCB, 0 = 0x00, 113 = 0x71, 7 = 0x07: the first byte is the lowest.
        Assert.Equal(new ConnectionAddress(0x077100CB, 2190), wire);
    }

    [Fact]
    public void TheWorkedOutAddressIsAnIPv4AddressSomethingCanReach()
    {
        var detected = AdvertisedAddress.DetectOutbound();

        Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, detected.AddressFamily);
        Assert.NotEqual(IPAddress.Any, detected);
    }

    [Theory]
    [InlineData("203.0.113.7", true)]
    [InlineData("1", false)]
    [InlineData("::1", false)]
    [InlineData("game.example.test", false)]
    public void OnlyAnIPv4AddressWrittenOutInFullCanBeConfigured(string text, bool accepted) =>
        Assert.Equal(accepted, AdvertisedAddress.IsIPv4(text));
}
