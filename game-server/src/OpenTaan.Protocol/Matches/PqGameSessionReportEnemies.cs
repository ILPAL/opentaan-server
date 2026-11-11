using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.Matches;

/// <summary>A client reports, as a match starts, the characters it counts as its enemies.</summary>
/// <remarks>
/// <para>
/// Every client sends it once for every match it starts, and expects no answer. The
/// characters it names are those not on the team of the room's lowest occupied slot,
/// whoever the reporting player is, so every client in a room names the same ones, and in
/// a match where everyone is on one team, as in a mission, it names none.
/// </para>
/// <para>
/// A room has <see cref="GameSlotInfo.SlotCount"/> slots, so a report naming more
/// characters than that comes from no real client, and is refused.
/// </para>
/// </remarks>
/// <param name="Enemies">The characters named, in the order of their slots.</param>
[WireClass("PqGameSessionReportEnemies")]
public sealed record PqGameSessionReportEnemies(IReadOnlyList<ReportedEnemy> Enemies) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PqGameSessionReportEnemies>.RegistryId;

    /// <summary>The characters named, in the order of their slots.</summary>
    public IReadOnlyList<ReportedEnemy> Enemies { get; } = Enemies ?? throw new ArgumentNullException(nameof(Enemies));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PqGameSessionReportEnemies> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var enemies = reader.ReadList(ReportedEnemy.Size, ReportedEnemy.ReadFrom);
        if (enemies.Length > GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.CountOutOfRange);
        }

        return reader.Finish(new PqGameSessionReportEnemies(enemies));
    }

    /// <summary>Writes the body, as the client would.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Enemies, static (list, enemy) => enemy.WriteTo(list));
    }

    /// <summary>Whether both name the same characters, with the same values, in the same order.</summary>
    public bool Equals(PqGameSessionReportEnemies? other) => other is not null && Enemies.SequenceEqual(other.Enemies);

    /// <summary>A hash of every entry, consistent with <see cref="Equals(PqGameSessionReportEnemies)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var enemy in Enemies)
        {
            hash.Add(enemy);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One character a client counts as its enemy.</summary>
/// <param name="CharacterId">The character.</param>
/// <param name="Value">A value the client keeps about the character, not yet identified; 0 when it keeps none.</param>
public sealed record ReportedEnemy(uint CharacterId, uint Value)
{
    /// <summary>The bytes one occupies: two 32-bit values, with no id of its own.</summary>
    internal const int Size = 2 * sizeof(uint);

    internal static ReportedEnemy ReadFrom(ref PacketReader reader)
    {
        var characterId = reader.ReadUInt32();
        var value = reader.ReadUInt32();
        return new ReportedEnemy(characterId, value);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(CharacterId);
        writer.WriteUInt32(Value);
    }
}
