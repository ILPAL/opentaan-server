namespace OpenTaan.Protocol.GamePlay;

/// <summary>A shot as the shooter's client worked it out: every step of it, which every client plays back the same way.</summary>
/// <remarks>
/// <para>
/// The shooter's client computes the whole shot, the missiles' flights, the hits, the
/// deaths and anything a missile drops or catches, before it plays it; the others play it
/// from this record alone. So the record goes to them exactly as it came, which is what
/// this codec reads and writes: every field, in order, with nothing left out.
/// </para>
/// <para>
/// Both of its lists hold steps of any kind; which list a hit goes in depends on the
/// weapon. The client's last step is always the base kind's mark that the shot is over,
/// a second after everything else, and each client reports the shot finished when it
/// reaches that mark.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The shooter's slot as its client wrote it; every client replaces it with the slot the shot was sent for.</param>
/// <param name="ActionType">The kind of action, as the client numbers it.</param>
/// <param name="Commands">The steps of the shot, in the order the client plays them.</param>
/// <param name="DelayTimings">Delays the client keeps with the shot.</param>
/// <param name="Results">More steps, for the missiles' results.</param>
/// <param name="WeaponFlags">Flags of the weapon that fired, as the client numbers them.</param>
/// <param name="InitialAngle">The angle the shot was fired at.</param>
/// <param name="HitSlots">The slots of the characters the shot reached.</param>
/// <param name="Seed">The value the shot's random parts were drawn from.</param>
[WireClass("GameScenario")]
public sealed record GameScenario(
    byte SlotIndex,
    byte ActionType,
    IReadOnlyList<GameScenarioCommand> Commands,
    IReadOnlyList<ushort> DelayTimings,
    IReadOnlyList<GameScenarioCommand> Results,
    ushort WeaponFlags,
    uint InitialAngle,
    IReadOnlyList<byte> HitSlots,
    uint Seed)
{
    /// <summary>The fewest bytes one occupies, its id included: one with every list empty.</summary>
    internal const int MinimumSize = sizeof(uint) + (2 * sizeof(byte)) + (3 * sizeof(uint)) + sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(uint);

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameScenario>.RegistryId;

    /// <summary>The steps of the shot, in the order the client plays them.</summary>
    public IReadOnlyList<GameScenarioCommand> Commands { get; } = Commands ?? throw new ArgumentNullException(nameof(Commands));

    /// <summary>Delays the client keeps with the shot.</summary>
    public IReadOnlyList<ushort> DelayTimings { get; } = DelayTimings ?? throw new ArgumentNullException(nameof(DelayTimings));

    /// <summary>More steps, for the missiles' results.</summary>
    public IReadOnlyList<GameScenarioCommand> Results { get; } = Results ?? throw new ArgumentNullException(nameof(Results));

    /// <summary>The slots of the characters the shot reached.</summary>
    public IReadOnlyList<byte> HitSlots { get; } = HitSlots ?? throw new ArgumentNullException(nameof(HitSlots));

    /// <summary>Whether both are the same shot: every field equal, and every list equal in order.</summary>
    public bool Equals(GameScenario? other) =>
        other is not null
        && SlotIndex == other.SlotIndex
        && ActionType == other.ActionType
        && Commands.SequenceEqual(other.Commands)
        && DelayTimings.SequenceEqual(other.DelayTimings)
        && Results.SequenceEqual(other.Results)
        && WeaponFlags == other.WeaponFlags
        && InitialAngle == other.InitialAngle
        && HitSlots.SequenceEqual(other.HitSlots)
        && Seed == other.Seed;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(SlotIndex, ActionType, Commands.Count, Results.Count, InitialAngle, Seed);

    internal static GameScenario ReadFrom(ref PacketReader reader)
    {
        reader.ExpectRegistryId(RegistryId);
        var slotIndex = reader.ReadByte();
        var actionType = reader.ReadByte();
        var commands = reader.ReadList(GameScenarioCommand.MinimumSize, GameScenarioCommand.ReadFrom);
        var delayTimings = reader.ReadList(sizeof(ushort), static (ref PacketReader list) => list.ReadUInt16());
        var results = reader.ReadList(GameScenarioCommand.MinimumSize, GameScenarioCommand.ReadFrom);
        var weaponFlags = reader.ReadUInt16();
        var initialAngle = reader.ReadUInt32();
        var hitSlots = reader.ReadCountedBytes().ToArray();
        var seed = reader.ReadUInt32();
        return new GameScenario(slotIndex, actionType, commands, delayTimings, results, weaponFlags, initialAngle, hitSlots, seed);
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(RegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(ActionType);
        writer.WriteList(Commands, static (list, command) => command.WriteTo(list));
        writer.WriteList(DelayTimings, static (list, delay) => list.WriteUInt16(delay));
        writer.WriteList(Results, static (list, command) => command.WriteTo(list));
        writer.WriteUInt16(WeaponFlags);
        writer.WriteUInt32(InitialAngle);
        writer.WriteCountedBytes([.. HitSlots]);
        writer.WriteUInt32(Seed);
    }
}
