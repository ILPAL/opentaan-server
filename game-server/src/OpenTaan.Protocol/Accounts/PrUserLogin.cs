using System.Buffers;

namespace OpenTaan.Protocol.Accounts;

/// <summary>
/// The answer to <see cref="PqUserLogin"/>: either <see cref="PrUserLoginSucceeded"/>
/// or <see cref="PrUserLoginFailed"/>.
/// </summary>
/// <remarks>
/// The two share a registry id and a leading flag, and differ in everything after it.
/// They are separate types because the failure is a different layout, not the success
/// layout with the flag cleared: the client reads four fields after a cleared flag, so
/// the success fields sent with the flag cleared leave bytes it never consumes, and it
/// stops receiving without disconnecting or reporting an error.
/// </remarks>
[WireClass("PrUserLogin")]
public abstract record PrUserLogin
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PrUserLogin>.RegistryId;

    private protected PrUserLogin()
    {
    }

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PrUserLogin> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        PrUserLogin packet = reader.ReadBoolean()
            ? PrUserLoginSucceeded.ReadFields(ref reader)
            : PrUserLoginFailed.ReadFields(ref reader);
        return reader.Finish(packet);
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        WriteFields(writer);
    }

    private protected abstract void WriteFields(PacketWriter writer);
}
