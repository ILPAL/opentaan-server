using OpenTaan.Protocol.Accounts;

namespace OpenTaan.Protocol.Characters;

/// <summary>How a character looks, as the character lists carry it.</summary>
/// <remarks>
/// <para>
/// Fields whose meaning is not established yet are named after where the client keeps
/// them, such as <see cref="Field08"/>.
/// </para>
/// <para>
/// The client can attach one more object of any type after the parts, behind a
/// presence flag. Nothing this server sends needs it, so it is never sent, and an
/// appearance that carries one is refused as <see cref="PacketError.Unsupported"/>:
/// its size cannot be known without reading it.
/// </para>
/// </remarks>
/// <param name="Gender">The character's gender, which decides the body drawn under the parts.</param>
/// <param name="Field08">Not read when the client draws the character, as far as is known.</param>
/// <param name="Field0C">A key the client looks up for one of the parts. Meaning not established yet.</param>
/// <param name="Slots">The parts, exactly <see cref="AppearanceSlot.Count"/> of them.</param>
[WireClass("CharacterAppear")]
public sealed record CharacterAppear(Gender Gender, uint Field08, ushort Field0C, IReadOnlyList<AppearanceSlot> Slots)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<CharacterAppear>.RegistryId;

    /// <summary>Bytes it occupies: its id, three fields, the parts, and the cleared presence flag.</summary>
    internal const int Size = sizeof(uint) + sizeof(byte) + sizeof(uint) + sizeof(ushort) + AppearanceSlot.TotalSize + sizeof(byte);

    /// <summary>The parts, exactly <see cref="AppearanceSlot.Count"/> of them.</summary>
    public IReadOnlyList<AppearanceSlot> Slots { get; } = AppearanceSlot.Complete(Slots, nameof(Slots));

    /// <summary>Whether <paramref name="other"/> looks the same: the same fields, and the same parts in the same order.</summary>
    /// <remarks>
    /// The parts are compared one by one. A record compares a list by reference, so
    /// without this an appearance read from bytes would never equal the one they were
    /// written from, nor would anything that holds one, such as a room's slot.
    /// </remarks>
    public bool Equals(CharacterAppear? other) =>
        other is not null
        && Gender == other.Gender
        && Field08 == other.Field08
        && Field0C == other.Field0C
        && Slots.SequenceEqual(other.Slots);

    /// <summary>A hash of the fields and every part, consistent with <see cref="Equals(CharacterAppear)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Gender);
        hash.Add(Field08);
        hash.Add(Field0C);
        foreach (var slot in Slots)
        {
            hash.Add(slot);
        }

        return hash.ToHashCode();
    }

    internal static CharacterAppear ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var gender = (Gender)reader.ReadByte();
        var field08 = reader.ReadUInt32();
        var field0C = reader.ReadUInt16();
        var slots = AppearanceSlot.ReadAll(ref reader);
        if (reader.ReadBoolean())
        {
            reader.Fail(PacketError.Unsupported);
        }

        return new CharacterAppear(gender, field08, field0C, slots);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte((byte)Gender);
        writer.WriteUInt32(Field08);
        writer.WriteUInt16(Field0C);
        AppearanceSlot.WriteAll(writer, Slots);
        writer.WriteBoolean(false);
    }
}
