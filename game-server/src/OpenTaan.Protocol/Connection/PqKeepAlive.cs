namespace OpenTaan.Protocol.Connection;

/// <summary>
/// The client's heartbeat, sent every 60 seconds on every connection it holds open.
/// </summary>
/// <remarks>
/// It is never answered: the client sends it and does not wait for anything back. Its
/// only use to the server is as proof that the connection is alive, which any frame
/// gives equally, so it is recognised by its id and never decoded.
/// </remarks>
[WireClass("PqKeepAlive")]
public sealed class PqKeepAlive
{
    private PqKeepAlive()
    {
    }

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqKeepAlive>.RegistryId;
}
