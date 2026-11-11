using System.Buffers;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A player gives up a match, for every character it plays. It carries nothing but its id.</summary>
/// <remarks>
/// <para>
/// The client lets a player give up once a match has run for two minutes. It marks all of
/// its own characters as given up and sends this, with no character named: giving up is
/// for the whole connection. It waits on no answer.
/// </para>
/// <para>
/// The client has no handler for this packet coming from the server, so a copy sent back
/// would change nothing on any screen, and this server sends none.
/// </para>
/// </remarks>
[WireClass("PcGamePlay_GiveUp")]
public sealed record PcGamePlayGiveUp : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayGiveUp>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayGiveUp> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        return reader.Finish(new PcGamePlayGiveUp());
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination) => new PacketWriter(destination).WriteUInt32(RegistryId);
}
