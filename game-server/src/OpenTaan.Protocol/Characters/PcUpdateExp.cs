using System.Buffers;

namespace OpenTaan.Protocol.Characters;

/// <summary>A character's level and experience, as they are now: sent to its own connection after they change.</summary>
/// <remarks>
/// <para>
/// The client keeps all four values as they come, in place of its own, for the character in
/// the slot the packet names, and draws the experience bar from them as it does at login:
/// the experience between the level's beginning and the next's. Nothing is redrawn at once;
/// the next screen that shows the character shows them.
/// </para>
/// <para>
/// The slot is the account's place in the connection's login, 0 or, for a second account,
/// 1, never a room's slot. The client ignores a packet for a slot it has no character in.
/// </para>
/// </remarks>
/// <param name="CharacterSlot">The account's place in the login, whose character this is.</param>
/// <param name="Level">The character's level.</param>
/// <param name="CurrentExperience">The character's experience, a running total.</param>
/// <param name="ExperienceFloor">The experience at which the level begins.</param>
/// <param name="ExperienceCeiling">The experience at which the next level begins. Above the floor: the client divides by the difference.</param>
[WireClass("PcUpdateExp")]
public sealed record PcUpdateExp(byte CharacterSlot, ushort Level, uint CurrentExperience, uint ExperienceFloor, uint ExperienceCeiling) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcUpdateExp>.RegistryId;

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcUpdateExp> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var characterSlot = reader.ReadByte();
        var level = reader.ReadUInt16();
        var currentExperience = reader.ReadUInt32();
        var experienceFloor = reader.ReadUInt32();
        var experienceCeiling = reader.ReadUInt32();
        return reader.Finish(new PcUpdateExp(characterSlot, level, currentExperience, experienceFloor, experienceCeiling));
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(CharacterSlot);
        writer.WriteUInt16(Level);
        writer.WriteUInt32(CurrentExperience);
        writer.WriteUInt32(ExperienceFloor);
        writer.WriteUInt32(ExperienceCeiling);
    }
}
