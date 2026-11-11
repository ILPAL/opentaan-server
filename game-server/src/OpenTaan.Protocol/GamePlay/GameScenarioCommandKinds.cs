namespace OpenTaan.Protocol.GamePlay;

/// <summary>A step that plays an action on its character, such as aiming or charging a shot; the client reads its two values by the step's type.</summary>
/// <param name="SlotIndex">The slot of the character the step concerns.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the step happens, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="Param1">The step's first value.</param>
/// <param name="Param2">The step's second value.</param>
[WireClass("GameScenarioCommand_Normal")]
public sealed record GameScenarioCommandNormal(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration, uint Param1, uint Param2)
    : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandNormal>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandNormal(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration, reader.ReadUInt32(), reader.ReadUInt32());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteUInt32(Param1);
        writer.WriteUInt32(Param2);
    }
}

/// <summary>A hit: health taken from a character, which every client applies as it plays the shot back.</summary>
/// <param name="SlotIndex">The slot of the character hit.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the hit lands, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the hit lasts, in milliseconds.</param>
/// <param name="DamageAmount">The health the hit takes.</param>
/// <param name="TargetHpBeforeHit">The character's health before the hit, as the shooter's client saw it.</param>
/// <param name="DelayValue">A value the client adds up for each character once the shot is over.</param>
/// <param name="StaggerValue">Another value the client adds up for each character once the shot is over.</param>
[WireClass("GameScenarioCommand_Damage")]
public sealed record GameScenarioCommandDamage(
    byte SlotIndex,
    byte CommandType,
    uint TimingTick,
    uint Duration,
    uint DamageAmount,
    uint TargetHpBeforeHit,
    ushort DelayValue,
    ushort StaggerValue) : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandDamage>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandDamage(
            common.SlotIndex,
            common.CommandType,
            common.TimingTick,
            common.Duration,
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt16(),
            reader.ReadUInt16());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteUInt32(DamageAmount);
        writer.WriteUInt32(TargetHpBeforeHit);
        writer.WriteUInt16(DelayValue);
        writer.WriteUInt16(StaggerValue);
    }
}

/// <summary>An item box dropped on the field where the shooter's client placed it.</summary>
/// <param name="SlotIndex">The slot of the character the box is credited to.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the box appears, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="ItemBoxId">The box's id, which a catch of it names.</param>
/// <param name="SpawnX">Where the box appears, across the map.</param>
/// <param name="SpawnY">Where the box appears, down the map.</param>
[WireClass("GameScenarioCommand_MakeItemBox")]
public sealed record GameScenarioCommandMakeItemBox(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration, ulong ItemBoxId, int SpawnX, int SpawnY)
    : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandMakeItemBox>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandMakeItemBox(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration, reader.ReadUInt64(), reader.ReadInt32(), reader.ReadInt32());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteUInt64(ItemBoxId);
        writer.WriteInt32(SpawnX);
        writer.WriteInt32(SpawnY);
    }
}

/// <summary>An item box caught during the shot, and the item it gives, as the catching character's client drew it.</summary>
/// <param name="SlotIndex">The slot of the character that catches the box.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the box is caught, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="ItemBoxId">The box caught, by the id it appeared with.</param>
/// <param name="PickupItemId">The item the box gives.</param>
/// <param name="Reserved">A value the client writes and never reads.</param>
[WireClass("GameScenarioCommand_EatItemBox")]
public sealed record GameScenarioCommandEatItemBox(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration, ulong ItemBoxId, ushort PickupItemId, uint Reserved)
    : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandEatItemBox>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandEatItemBox(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration, reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadUInt32());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteUInt64(ItemBoxId);
        writer.WriteUInt16(PickupItemId);
        writer.WriteUInt32(Reserved);
    }
}

/// <summary>A change to a team's gauge, which fills as a team deals damage.</summary>
/// <param name="SlotIndex">The slot of the character whose team's gauge changes.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the gauge changes, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="GaugeType">Which of the client's gauge counts the change is for.</param>
/// <param name="NewGaugeValue">The gauge after the change.</param>
/// <param name="PreviousGaugeValue">The gauge before the change.</param>
[WireClass("GameScenarioCommand_TeamGauge")]
public sealed record GameScenarioCommandTeamGauge(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration, byte GaugeType, ushort NewGaugeValue, ushort PreviousGaugeValue)
    : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandTeamGauge>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandTeamGauge(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration, reader.ReadByte(), reader.ReadUInt16(), reader.ReadUInt16());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteByte(GaugeType);
        writer.WriteUInt16(NewGaugeValue);
        writer.WriteUInt16(PreviousGaugeValue);
    }
}

/// <summary>A bonus item used during the shot.</summary>
/// <param name="SlotIndex">The slot of the character using the item.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the item is used, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="ItemId">The item used.</param>
[WireClass("GameScenarioCommand_UseBonusGameItem")]
public sealed record GameScenarioCommandUseBonusGameItem(byte SlotIndex, byte CommandType, uint TimingTick, uint Duration, ushort ItemId)
    : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandUseBonusGameItem>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common) =>
        new GameScenarioCommandUseBonusGameItem(common.SlotIndex, common.CommandType, common.TimingTick, common.Duration, reader.ReadUInt16());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer) => writer.WriteUInt16(ItemId);
}
