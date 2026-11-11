namespace OpenTaan.Protocol.GamePlay;

/// <summary>A missile's flight in a shot: where it lands, when it was fired, and how hard it hits.</summary>
/// <remarks>
/// The guided and the fired kinds extend it, adding their own fields after its own, so a
/// missile's flight is read and written in one place for all three.
/// </remarks>
/// <param name="SlotIndex">The slot of the character that fired.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the step happens, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="MissileSlotIndex">Which of the shot's missiles this is.</param>
/// <param name="MissileType">The kind of missile, as the client numbers it.</param>
/// <param name="ImpactX">Where the missile lands, across the map.</param>
/// <param name="ImpactY">Where the missile lands, down the map.</param>
/// <param name="FireStartTick">When the missile was fired, in milliseconds from the start of the shot.</param>
/// <param name="DamageMultiplier">What the missile's damage is multiplied by.</param>
/// <param name="SpecialTrajectory">Not zero for a missile that does not fly a plain arc; kept as sent.</param>
[WireClass("GameScenarioCommand_Weapon")]
public record GameScenarioCommandWeapon(
    byte SlotIndex,
    byte CommandType,
    uint TimingTick,
    uint Duration,
    byte MissileSlotIndex,
    byte MissileType,
    int ImpactX,
    int ImpactY,
    uint FireStartTick,
    float DamageMultiplier,
    byte SpecialTrajectory) : GameScenarioCommand(SlotIndex, CommandType, TimingTick, Duration)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandWeapon>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameScenarioCommand ReadRest(ref PacketReader reader, in Common common)
    {
        var flight = Flight.Read(ref reader);
        return new GameScenarioCommandWeapon(
            common.SlotIndex,
            common.CommandType,
            common.TimingTick,
            common.Duration,
            flight.MissileSlotIndex,
            flight.MissileType,
            flight.ImpactX,
            flight.ImpactY,
            flight.FireStartTick,
            flight.DamageMultiplier,
            flight.SpecialTrajectory);
    }

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteByte(MissileSlotIndex);
        writer.WriteByte(MissileType);
        writer.WriteInt32(ImpactX);
        writer.WriteInt32(ImpactY);
        writer.WriteUInt32(FireStartTick);
        writer.WriteSingle(DamageMultiplier);
        writer.WriteByte(SpecialTrajectory);
    }

    /// <summary>The fields of a missile's flight, read before the ones a guided or a fired missile adds.</summary>
    internal readonly record struct Flight(
        byte MissileSlotIndex,
        byte MissileType,
        int ImpactX,
        int ImpactY,
        uint FireStartTick,
        float DamageMultiplier,
        byte SpecialTrajectory)
    {
        public static Flight Read(ref PacketReader reader) => new(
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadSingle(),
            reader.ReadByte());
    }
}

/// <summary>A guided missile's flight, and the point it was guided to.</summary>
/// <param name="SlotIndex">The slot of the character that fired.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the step happens, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="MissileSlotIndex">Which of the shot's missiles this is.</param>
/// <param name="MissileType">The kind of missile, as the client numbers it.</param>
/// <param name="ImpactX">Where the missile lands, across the map.</param>
/// <param name="ImpactY">Where the missile lands, down the map.</param>
/// <param name="FireStartTick">When the missile was fired, in milliseconds from the start of the shot.</param>
/// <param name="DamageMultiplier">What the missile's damage is multiplied by.</param>
/// <param name="SpecialTrajectory">Not zero for a missile that does not fly a plain arc; kept as sent.</param>
/// <param name="TargetX">Where the missile was guided to, across the map.</param>
/// <param name="TargetY">Where the missile was guided to, down the map.</param>
[WireClass("GameScenarioCommand_Guided")]
public sealed record GameScenarioCommandGuided(
    byte SlotIndex,
    byte CommandType,
    uint TimingTick,
    uint Duration,
    byte MissileSlotIndex,
    byte MissileType,
    int ImpactX,
    int ImpactY,
    uint FireStartTick,
    float DamageMultiplier,
    byte SpecialTrajectory,
    int TargetX,
    int TargetY)
    : GameScenarioCommandWeapon(SlotIndex, CommandType, TimingTick, Duration, MissileSlotIndex, MissileType, ImpactX, ImpactY, FireStartTick, DamageMultiplier, SpecialTrajectory)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandGuided>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static new GameScenarioCommand ReadRest(ref PacketReader reader, in Common common)
    {
        var flight = Flight.Read(ref reader);
        return new GameScenarioCommandGuided(
            common.SlotIndex,
            common.CommandType,
            common.TimingTick,
            common.Duration,
            flight.MissileSlotIndex,
            flight.MissileType,
            flight.ImpactX,
            flight.ImpactY,
            flight.FireStartTick,
            flight.DamageMultiplier,
            flight.SpecialTrajectory,
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        base.WriteRest(writer);
        writer.WriteInt32(TargetX);
        writer.WriteInt32(TargetY);
    }
}

/// <summary>A fired missile's flight, with the weapon that fired it and where it exploded and came to rest.</summary>
/// <param name="SlotIndex">The slot of the character that fired.</param>
/// <param name="CommandType">What the step does, as the client numbers it.</param>
/// <param name="TimingTick">When the step happens, in milliseconds from the start of the shot.</param>
/// <param name="Duration">How long the step lasts, in milliseconds.</param>
/// <param name="MissileSlotIndex">Which of the shot's missiles this is.</param>
/// <param name="MissileType">The kind of missile, as the client numbers it.</param>
/// <param name="ImpactX">Where the missile lands, across the map.</param>
/// <param name="ImpactY">Where the missile lands, down the map.</param>
/// <param name="FireStartTick">When the missile was fired, in milliseconds from the start of the shot.</param>
/// <param name="DamageMultiplier">What the missile's damage is multiplied by.</param>
/// <param name="SpecialTrajectory">Not zero for a missile that does not fly a plain arc; kept as sent.</param>
/// <param name="WeaponId">The weapon that fired the missile.</param>
/// <param name="DamageType">The kind of damage the missile does, as the client numbers it.</param>
/// <param name="ExplosionX">Where the missile exploded, across the map.</param>
/// <param name="ExplosionY">Where the missile exploded, down the map.</param>
/// <param name="LandingX">Where the missile came to rest, across the map.</param>
/// <param name="LandingY">Where the missile came to rest, down the map.</param>
/// <param name="TerrainFlag">How the missile met the ground, as the client numbers it.</param>
/// <param name="ExtraData">Never set by the client: whatever its memory held, kept as sent.</param>
[WireClass("GameScenarioCommand_Fire")]
public sealed record GameScenarioCommandFire(
    byte SlotIndex,
    byte CommandType,
    uint TimingTick,
    uint Duration,
    byte MissileSlotIndex,
    byte MissileType,
    int ImpactX,
    int ImpactY,
    uint FireStartTick,
    float DamageMultiplier,
    byte SpecialTrajectory,
    ushort WeaponId,
    byte DamageType,
    int ExplosionX,
    int ExplosionY,
    int LandingX,
    int LandingY,
    byte TerrainFlag,
    ushort ExtraData)
    : GameScenarioCommandWeapon(SlotIndex, CommandType, TimingTick, Duration, MissileSlotIndex, MissileType, ImpactX, ImpactY, FireStartTick, DamageMultiplier, SpecialTrajectory)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static new uint RegistryId => WireIdentity<GameScenarioCommandFire>.RegistryId;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static new GameScenarioCommand ReadRest(ref PacketReader reader, in Common common)
    {
        var flight = Flight.Read(ref reader);
        return new GameScenarioCommandFire(
            common.SlotIndex,
            common.CommandType,
            common.TimingTick,
            common.Duration,
            flight.MissileSlotIndex,
            flight.MissileType,
            flight.ImpactX,
            flight.ImpactY,
            flight.FireStartTick,
            flight.DamageMultiplier,
            flight.SpecialTrajectory,
            reader.ReadUInt16(),
            reader.ReadByte(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadByte(),
            reader.ReadUInt16());
    }

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        base.WriteRest(writer);
        writer.WriteUInt16(WeaponId);
        writer.WriteByte(DamageType);
        writer.WriteInt32(ExplosionX);
        writer.WriteInt32(ExplosionY);
        writer.WriteInt32(LandingX);
        writer.WriteInt32(LandingY);
        writer.WriteByte(TerrainFlag);
        writer.WriteUInt16(ExtraData);
    }
}
