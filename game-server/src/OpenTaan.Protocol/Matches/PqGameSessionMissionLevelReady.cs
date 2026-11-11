using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A client in a mission reports that a level's opening is over and its enemies are placed.</summary>
/// <remarks>
/// Every client in the match sends it once for each level announced to it, and waits on
/// nothing; the level's first turn starts once the clients have reported.
/// </remarks>
/// <param name="Level">The level that is ready.</param>
[WireClass("PqGameSession_MissionLevelReady")]
public sealed record PqGameSessionMissionLevelReady(byte Level) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionMissionLevelReady>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionMissionLevelReady> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var level = reader.ReadByte();
        return reader.Finish(new PqGameSessionMissionLevelReady(level));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Level);
    }
}
