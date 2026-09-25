namespace OpenTaan.Protocol.Connection;

/// <summary>Telemetry the client sends at points of its own choosing, in any state.</summary>
/// <remarks>
/// It is never answered, and this server has no use for its contents yet, so it is
/// recognised by its id and discarded without being decoded. Refusing it would close
/// connections over traffic every real client sends.
/// </remarks>
[WireClass("PcLogging")]
public sealed class PcLogging
{
    private PcLogging()
    {
    }

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcLogging>.RegistryId;
}
