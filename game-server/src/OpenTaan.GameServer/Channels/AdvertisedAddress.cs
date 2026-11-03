using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using OpenTaan.Protocol.Accounts;

namespace OpenTaan.GameServer.Channels;

/// <summary>How the address the client is told was chosen.</summary>
public enum AddressSource
{
    /// <summary>Set in configuration.</summary>
    Configured,

    /// <summary>The address the server binds to, which names one.</summary>
    BindAddress,

    /// <summary>
    /// Worked out from the network interface this machine sends from. Right on a local
    /// network; behind a router or in a container, an address the outside cannot reach.
    /// </summary>
    Detected,
}

/// <summary>The address the client is told the server's channels are at.</summary>
/// <param name="Address">An IPv4 address.</param>
/// <param name="Port">The TCP port.</param>
/// <param name="Source">How the address was chosen.</param>
public sealed record AdvertisedAddress(IPAddress Address, ushort Port, AddressSource Source)
{
    // TEST-NET-1 (RFC 5737): nothing answers there, and nothing is sent to find a route to it.
    private static readonly IPEndPoint RouteProbe = new(IPAddress.Parse("192.0.2.1"), 9);

    /// <summary>The address as the client carries it.</summary>
    public ConnectionAddress ToWire() =>
        // The client holds the address's bytes in network order, read as a little-endian value.
        new(BinaryPrimitives.ReadUInt32LittleEndian(Address.GetAddressBytes()), Port);

    /// <summary>
    /// The configured address if there is one; otherwise the bind address if it names one;
    /// otherwise what <paramref name="detect"/> finds.
    /// </summary>
    public static AdvertisedAddress Choose(string? configured, string bindAddress, ushort port, Func<IPAddress> detect)
    {
        ArgumentNullException.ThrowIfNull(detect);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new AdvertisedAddress(IPAddress.Parse(configured), port, AddressSource.Configured);
        }

        if (IPAddress.TryParse(bindAddress, out var bound)
            && bound.AddressFamily == AddressFamily.InterNetwork
            && !bound.Equals(IPAddress.Any))
        {
            return new AdvertisedAddress(bound, port, AddressSource.BindAddress);
        }

        return new AdvertisedAddress(detect(), port, AddressSource.Detected);
    }

    /// <summary>
    /// The IPv4 address of the network interface this machine would send from, or the
    /// loopback address when it has no route at all.
    /// </summary>
    public static IPAddress DetectOutbound()
    {
        try
        {
            // Connecting a datagram socket sends nothing: it only asks for a route.
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(RouteProbe);
            return ((IPEndPoint)probe.LocalEndPoint!).Address;
        }
        catch (SocketException)
        {
            return IPAddress.Loopback;
        }
    }

    /// <summary>Whether <paramref name="text"/> is an IPv4 address written out in full.</summary>
    public static bool IsIPv4(string text) =>
        IPAddress.TryParse(text, out var address)
        && address.AddressFamily == AddressFamily.InterNetwork
        && address.ToString() == text;
}
