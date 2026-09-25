namespace OpenTaan.Protocol.Connection;

/// <summary>
/// The client's first request on the connection it logs in on, sent before the login
/// itself and answered with <see cref="PrFirstPacket"/>.
/// </summary>
/// <remarks>
/// It describes the client: a version, two machine identifiers, cached data and a hash
/// of it. Two readings of its layout disagree about the cached data, and no recorded
/// session carries one to settle it, so it is recognised by its id and answered without
/// being decoded. Nothing in it is needed to log in.
/// </remarks>
[WireClass("PqFirstPacket")]
public sealed class PqFirstPacket
{
    private PqFirstPacket()
    {
    }

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqFirstPacket>.RegistryId;
}
