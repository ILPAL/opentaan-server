namespace OpenTaan.Protocol.Rooms;

/// <summary>Who hosts a room: sent with the room on entering it, and again whenever the host changes.</summary>
/// <remarks>
/// <para>
/// A client takes itself for the host when <see cref="HostPlayerId"/> is the player id its
/// login was answered with. It then shows the host's buttons, and all of that
/// connection's characters are the host's, since they share that id.
/// </para>
/// <para>
/// The client also puts a label on each of the host's slots: one label on the slot whose
/// character id is <see cref="Field08"/>, another on the host's other slots. What the two
/// labels show is not established yet. The reference sessions carry the host's player id
/// in both fields, which matches no character id, so every one of the host's slots gets
/// the second label; this server does the same.
/// </para>
/// </remarks>
/// <param name="Field08">The character whose slot gets the host's first label. See the remarks.</param>
/// <param name="HostPlayerId">The host's player id: the id its login was answered with.</param>
[WireClass("GameSessionOption")]
public sealed record GameSessionOption(uint Field08, uint HostPlayerId)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameSessionOption>.RegistryId;

    internal static GameSessionOption ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        return new GameSessionOption(reader.ReadUInt32(), reader.ReadUInt32());
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteUInt32(Field08);
        writer.WriteUInt32(HostPlayerId);
    }
}
