using System.Buffers;

namespace OpenTaan.Protocol.Matches;

/// <summary>A client in a mission asks for the mission's next level.</summary>
/// <remarks>
/// <para>
/// Every client in a mission asks for level 1 once it has loaded the match, and for the
/// next level once its own count of the enemies killed reaches the level's target. Each
/// client asks for each level once, so in a room of several players every level is asked
/// for several times. When the mission has no next level, the client asks for
/// <see cref="NoNextLevel"/>: the mission is cleared.
/// </para>
/// <para>
/// The three values after the level come from the client's copy of the mission, and depend
/// on the mission's difficulty and on how many players are in the match. They are kept as
/// read; the server does not use them.
/// </para>
/// </remarks>
/// <param name="Level">The level asked for, from 1, or <see cref="NoNextLevel"/> once the last level is cleared.</param>
/// <param name="TileIndex">A value from the level's settings, or <see cref="NoValue"/> when there is no next level.</param>
/// <param name="EndValue">A value from the level's settings, or <see cref="NoValue"/> when there is no next level.</param>
/// <param name="IsDynamic">A flag from the level's settings, false when there is no next level.</param>
[WireClass("PqGameSession_StartMissionLevel")]
public sealed record PqGameSessionStartMissionLevel(byte Level, uint TileIndex, uint EndValue, bool IsDynamic) : IPacket
{
    /// <summary>The level a client asks for when the mission has no next one: the mission is cleared.</summary>
    public const byte NoNextLevel = 0xFF;

    /// <summary>What the level's two values are when there is no next level.</summary>
    public const uint NoValue = 0xFFFFFFFF;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionStartMissionLevel>.RegistryId;

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionStartMissionLevel> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var level = reader.ReadByte();
        var tileIndex = reader.ReadUInt32();
        var endValue = reader.ReadUInt32();
        var isDynamic = reader.ReadBoolean();
        return reader.Finish(new PqGameSessionStartMissionLevel(level, tileIndex, endValue, isDynamic));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Level);
        writer.WriteUInt32(TileIndex);
        writer.WriteUInt32(EndValue);
        writer.WriteBoolean(IsDynamic);
    }
}
