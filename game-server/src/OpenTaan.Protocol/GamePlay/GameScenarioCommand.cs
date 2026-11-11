using System.Collections.Frozen;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>One step of a shot, as every client plays it back: what happens, to which character, and when.</summary>
/// <remarks>
/// <para>
/// A shot is a list of these, which the shooter's client works out and every client plays
/// back at the times they give; see <see cref="GameScenario"/>. Each kind of step is a
/// class of its own on the wire, led by its own id, and adds its fields after the ones
/// every step has. This one, the base kind, carries only those, and the client uses it for
/// the mark that ends a shot.
/// </para>
/// <para>
/// The slot is a game slot for a player's character, 100 or more for one of a mission's
/// enemies, or another value the client skips, so it is read as it comes. Nothing here is
/// checked against the match: the client plays a shot's steps by itself, and the server
/// passes them on as they came.
/// </para>
/// </remarks>
/// <param name="SlotIndex">The slot of the character the step concerns.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the step happens, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
[WireClass("GameScenarioCommand")]
public record GameScenarioCommand(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration)
{
    /// <summary>The fewest bytes a step occupies, its id included: one of this base kind.</summary>
    internal const int MinimumSize = sizeof(uint) + (2 * sizeof(byte)) + (2 * sizeof(uint));

    /// <summary>A step put in place of one that could not be read; the read has failed by then.</summary>
    private static readonly GameScenarioCommand Unread = new(0, 0, 0, 0);

    // Reads what a kind adds after the fields every step has, by the kind's id.
    private static readonly FrozenDictionary<uint, KindReader> Kinds = new Dictionary<uint, KindReader>
    {
        [RegistryId] = static (ref PacketReader _, in Common common) => new GameScenarioCommand(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration),
        [GameScenarioCommandNormal.RegistryId] = GameScenarioCommandNormal.ReadRest,
        [GameScenarioCommandWeapon.RegistryId] = GameScenarioCommandWeapon.ReadRest,
        [GameScenarioCommandGuided.RegistryId] = GameScenarioCommandGuided.ReadRest,
        [GameScenarioCommandFire.RegistryId] = GameScenarioCommandFire.ReadRest,
        [GameScenarioCommandDamage.RegistryId] = GameScenarioCommandDamage.ReadRest,
        [GameScenarioCommandMakeItemBox.RegistryId] = GameScenarioCommandMakeItemBox.ReadRest,
        [GameScenarioCommandEatItemBox.RegistryId] = GameScenarioCommandEatItemBox.ReadRest,
        [GameScenarioCommandTeamGauge.RegistryId] = GameScenarioCommandTeamGauge.ReadRest,
        [GameScenarioCommandUseBonusGameItem.RegistryId] = GameScenarioCommandUseBonusGameItem.ReadRest,
    }.ToFrozenDictionary();

    /// <summary>Reads the fields one kind adds, after the ones every step has.</summary>
    internal delegate GameScenarioCommand KindReader(ref PacketReader reader, in Common common);

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameScenarioCommand>.RegistryId;

    /// <summary>The id that leads this kind, computed from its wire name.</summary>
    private protected virtual uint KindRegistryId => RegistryId;

    /// <summary>Reads a step of any kind a shot holds, or refuses the read.</summary>
    internal static GameScenarioCommand ReadFrom(ref PacketReader reader)
    {
        var id = reader.ReadUInt32();
        if (reader.Error != PacketError.None)
        {
            return Unread;
        }

        if (!Kinds.TryGetValue(id, out var readRest))
        {
            reader.Fail(PacketError.WrongRegistryId);
            return Unread;
        }

        var common = new Common(reader.ReadByte(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32());
        var command = readRest(ref reader, in common);
        return reader.Error == PacketError.None ? command : Unread;
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(KindRegistryId);
        writer.WriteByte(SlotIndex);
        writer.WriteByte(CommandType);
        writer.WriteUInt32(TimingTick);
        writer.WriteUInt32(Duration);
        WriteRest(writer);
    }

    /// <summary>Writes the fields this kind adds after the ones every step has; the base kind adds none.</summary>
    private protected virtual void WriteRest(PacketWriter writer)
    {
    }

    /// <summary>The fields every step has, read before the ones its kind adds.</summary>
    internal readonly record struct Common(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration);
}
