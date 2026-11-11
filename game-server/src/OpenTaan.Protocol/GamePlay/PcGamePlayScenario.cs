using System.Buffers;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>A shot: the turn's character fired, and here is everything the shot does.</summary>
/// <remarks>
/// <para>
/// The shooter's client plays the shot on its own screen at once and sends it; the server
/// passes it to every other client in the match, which plays it back. It never goes back
/// to the shooter: a client sends on any shot it is given that names one of its own
/// slots, so a copy back would come round again.
/// </para>
/// <para>
/// The firing slot is read as it comes. Every client copies it into the shot and acts on
/// it only where it names a character, so it is the server, not this codec, that checks
/// it names the character whose turn it is.
/// </para>
/// </remarks>
/// <param name="Direction">Which way the shooter faces.</param>
/// <param name="FiringSlot">The slot of the character that fired.</param>
/// <param name="Scenario">The shot.</param>
/// <param name="TotalStats">The sum of the shooter's stats, as its client reckons them.</param>
/// <param name="AmmoAddition">Ammunition the shooter is given back once the shot is played.</param>
/// <param name="Integrity">The client's report on its own code.</param>
[WireClass("PcGamePlay_Scenario")]
public sealed record PcGamePlayScenario(
    byte Direction,
    byte FiringSlot,
    GameScenario Scenario,
    uint TotalStats,
    ushort AmmoAddition,
    IntegrityReport Integrity) : IPacket
{
    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcGamePlayScenario>.RegistryId;

    /// <summary>The shot.</summary>
    public GameScenario Scenario { get; } = Scenario ?? throw new ArgumentNullException(nameof(Scenario));

    /// <summary>Reads a body the client sent.</summary>
    public static PacketReadResult<PcGamePlayScenario> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var direction = reader.ReadByte();
        var firingSlot = reader.ReadByte();
        var scenario = GameScenario.ReadFrom(ref reader);
        var totalStats = reader.ReadUInt32();
        var ammoAddition = reader.ReadUInt16();
        var integrity = IntegrityReport.ReadFrom(ref reader);
        return reader.Finish(new PcGamePlayScenario(direction, firingSlot, scenario, totalStats, ammoAddition, integrity));
    }

    /// <summary>Writes the body, as the shooter's client wrote it.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(Direction);
        writer.WriteByte(FiringSlot);
        Scenario.WriteTo(writer);
        writer.WriteUInt32(TotalStats);
        writer.WriteUInt16(AmmoAddition);
        Integrity.WriteTo(writer);
    }
}
