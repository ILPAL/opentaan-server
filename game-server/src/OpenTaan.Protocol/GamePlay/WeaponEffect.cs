namespace OpenTaan.Protocol.GamePlay;

/// <summary>An effect a shot leaves on the characters it hurts, for a number of turns.</summary>
/// <remarks>
/// <para>
/// Some weapons' missiles carry an effect, taken from the weapon's data in the client,
/// and the shooter's client puts each fired missile's effect in its shot; see
/// <see cref="GameScenario.Effects"/>. Every client playing the shot gives its effects
/// to each character a damage step hurts, other than the shooter, and counts them down
/// itself at the end of every turn: the server does nothing with them but pass them on.
/// </para>
/// <para>
/// The client keeps an effect only when its type is below 3 and at least one of its
/// counts is not zero, and skips the end of the turn the effect was given in. Its type 0
/// multiplies one of the character's gauges by <see cref="Amount"/>, at the start of the
/// character's own turns; type 1 takes the smallest amount of its kind; type 2 hurts the
/// character by <see cref="Amount"/> each time its kind of damage over time is applied,
/// which the client does only in missions, and never takes a character below 200 health.
/// Everything here is read as it comes: the client sorts out what it keeps.
/// </para>
/// </remarks>
/// <param name="EffectType">What the effect does, as the client numbers it.</param>
/// <param name="Amount">How strong the effect is: a factor for types 0 and 1, the damage each time for type 2.</param>
/// <param name="Param2">A second value from the weapon's data, which the client keeps with the effect.</param>
/// <param name="Turns">How many more ends of turns the effect lasts, whoever's turn it is; 0xFFFF for one that is never counted down.</param>
/// <param name="OwnTurns">How many more ends of its character's own turns the effect lasts; it ends once both counts are 0.</param>
[WireClass("WeaponEffect")]
public sealed record WeaponEffect(byte EffectType, float Amount, float Param2, ushort Turns, ushort OwnTurns)
{
    /// <summary>The bytes one occupies, its id included: every field has a fixed size.</summary>
    internal const int Size = sizeof(uint) + sizeof(byte) + (2 * sizeof(float)) + (2 * sizeof(ushort));

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<WeaponEffect>.RegistryId;

    internal static WeaponEffect ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        return new WeaponEffect(reader.ReadByte(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt16(), reader.ReadUInt16());
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(EffectType);
        writer.WriteSingle(Amount);
        writer.WriteSingle(Param2);
        writer.WriteUInt16(Turns);
        writer.WriteUInt16(OwnTurns);
    }
}
