using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A mission's next level begins: sent to everyone in the match.</summary>
/// <remarks>
/// <para>
/// Each client that receives it plays the level's opening, about two and a half seconds,
/// in which it places the level's enemies, and then reports with
/// <see cref="PqGameSessionMissionLevelReady"/> that the level is ready. Receiving it
/// again restarts the opening, so each level is announced once.
/// </para>
/// <para>
/// A turn that starts while a client is still in the opening cuts the opening short, and
/// that client never places the level's enemies; so a level's first turn waits for the
/// clients' reports.
/// </para>
/// </remarks>
/// <param name="Level">The level beginning, from 1.</param>
[WireClass("PcGameSession_StartMissionLevel")]
public sealed record PcGameSessionStartMissionLevel(byte Level) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGameSessionStartMissionLevel>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcGameSessionStartMissionLevel> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var level = reader.ReadByte();
        return reader.Finish(new PcGameSessionStartMissionLevel(level));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Level);
    }
}
