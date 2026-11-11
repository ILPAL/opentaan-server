using OpenTaan.Protocol;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Tests.Protocol;

/// <summary>
/// A match's turns: a shot and every kind of step in it, a shot played back, the end of a
/// turn, a pass, a walk, a place, a fall and a ghost. Every value here is invented, each
/// field's bytes unlike its neighbours', and every expected body is written out by hand
/// from the client's layout.
/// </summary>
public sealed class TurnCodecTests
{
    private static readonly GameScenarioCommand EndMark = new(0x05, 0x08, 0x06070809, 0x0A0B0C0D);

    private static readonly GameScenarioCommandNormal Normal = new(0x11, 0x0E, 0x12131415, 0x16171819, 0x1A1B1C1D, 0x1E1F2021);

    private static readonly GameScenarioCommandWeapon Weapon = new(0x21, 0x02, 0x22232425, 0x26272829, 0x2A, 0x2B, 0x2C2D2E2F, -0x30313233, 0x34353637, 1.5f, 0x38);

    private static readonly GameScenarioCommandGuided Guided = new(0x41, 0x02, 0x42434445, 0x46474849, 0x4A, 0x4B, 0x4C4D4E4F, 0x50515253, 0x54555657, -0.25f, 0x58, 0x595A5B5C, -0x5D5E5F60);

    private static readonly GameScenarioCommandFire Fire = new(
        0x61, 0x00, 0x62636465, 0x66676869, 0x6A, 0x6B, 0x6C6D6E6F, 0x70717273, 0x74757677, 2f, 0x78,
        0x797A, 0x7B, 0x7C7D7E7F, -0x01020304, 0x05060708, 0x090A0B0C, 0x0D, 0x0E0F);

    private static readonly GameScenarioCommandDamage Damage = new(0x64, 0x04, 0x11121314, 0x15161718, 0x191A1B1C, 0x1D1E1F20, 0x2122, 0x2324);

    private static readonly GameScenarioCommandMakeItemBox MakeItemBox = new(0x02, 0x0A, 0x25262728, 0x292A2B2C, 0x2D2E2F3031323334, -0x35363738, 0x393A3B3C);

    private static readonly GameScenarioCommandEatItemBox EatItemBox = new(0x03, 0x0B, 0x3D3E3F40, 0x41424344, 0x4546474849505152, 0x5354, 0x55565758);

    private static readonly GameScenarioCommandTeamGauge TeamGauge = new(0x04, 0x0C, 0x595A5B5C, 0x5D5E5F60, 0x61, 0x6263, 0x6465);

    private static readonly GameScenarioCommandUseBonusGameItem UseBonusGameItem = new(0x05, 0x0F, 0x66676869, 0x6A6B6C6D, 0x6E6F);

    private static readonly IntegrityReport Integrity = new(0x31323334, 0x35363738, 0x39, 0x3A3B3C3D, 0x3E3F4041, 0x42434445);

    [Fact]
    public void AShotIsWhichWayItsShooterFacesTheSlotTheShotTheStatsTheAmmunitionThenTheReport()
    {
        var shot = new PcGamePlayScenario(0x01, 0x02, Scenario(), 0x21222324, 0x2526, Integrity);

        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlayScenario.RegistryId),
            0x01,
            0x02,
            .. ScenarioBytes(),
            .. Encoded.UInt32(0x21222324),
            .. Encoded.UInt16(0x2526),
            .. Encoded.UInt32(0x31323334),
            .. Encoded.UInt32(0x35363738),
            0x39,
            .. Encoded.UInt32(0x3A3B3C3D),
            .. Encoded.UInt32(0x3E3F4041),
            .. Encoded.UInt32(0x42434445),
        ];

        Assert.Equal(expected, Encoded.Bytes(shot.Write));
        Assert.Equal(shot, PcGamePlayScenario.TryRead(expected).Packet);
    }

    [Fact]
    public void EveryKindOfStepReadsBackAsItsOwnKind()
    {
        var read = PcGamePlayScenario.TryRead(Encoded.Bytes(new PcGamePlayScenario(0, 0, Scenario(), 0, 0, Integrity).Write)).Packet!;

        Assert.Equal(
            [typeof(GameScenarioCommand), typeof(GameScenarioCommandNormal), typeof(GameScenarioCommandWeapon), typeof(GameScenarioCommandGuided), typeof(GameScenarioCommandFire)],
            read.Scenario.Commands.Select(command => command.GetType()));
        Assert.Equal(
            [typeof(GameScenarioCommandDamage), typeof(GameScenarioCommandMakeItemBox), typeof(GameScenarioCommandEatItemBox), typeof(GameScenarioCommandTeamGauge), typeof(GameScenarioCommandUseBonusGameItem)],
            read.Scenario.Results.Select(command => command.GetType()));
    }

    [Fact]
    public void StepsOfDifferentKindsAreNeverEqualEvenWithTheSameSharedFields()
    {
        var guided = new GameScenarioCommandGuided(0x21, 0x02, 0x22232425, 0x26272829, 0x2A, 0x2B, 0x2C2D2E2F, -0x30313233, 0x34353637, 1.5f, 0x38, 0, 0);

        Assert.NotEqual<GameScenarioCommand>(Weapon, guided);
        Assert.NotEqual(new GameScenarioCommand(0x11, 0x0E, 0x12131415, 0x16171819), Normal);
    }

    [Fact]
    public void AShotsFiringSlotAndItsStepsSlotsAreReadAsTheyCome()
    {
        // A slot no character has, as a shot made by a server names, and a mission enemy's.
        var shot = new PcGamePlayScenario(0, 0xFE, Scenario([new GameScenarioCommand(0x64, 0x05, 0, 0)], []), 0, 0, default);

        Assert.Equal(shot, PcGamePlayScenario.TryRead(Encoded.Bytes(shot.Write)).Packet);
    }

    [Fact]
    public void AStepOfAClassThatIsNoStepIsRefused()
    {
        var body = Encoded.Bytes(new PcGamePlayScenario(0, 0, Scenario([EndMark], []), 0, 0, Integrity).Write);

        // The end mark's id, after the shot's own id, the two bytes before its list and the count.
        var at = sizeof(uint) + 2 + sizeof(uint) + 2 + sizeof(uint);
        Encoded.UInt32(GameScenario.RegistryId).CopyTo(body, at);

        Assert.Equal(PacketError.WrongRegistryId, PcGamePlayScenario.TryRead(body).Error);
    }

    [Fact]
    public void AShotWithoutItsShotsIdIsRefused()
    {
        var body = Encoded.Bytes(new PcGamePlayScenario(0, 0, Scenario(), 0, 0, Integrity).Write);
        Encoded.UInt32(GameScenarioCommand.RegistryId).CopyTo(body, sizeof(uint) + 2);

        Assert.Equal(PacketError.WrongRegistryId, PcGamePlayScenario.TryRead(body).Error);
    }

    [Fact]
    public void AShotCountingMoreStepsThanItsBodyHoldsIsRefused()
    {
        byte[] body =
        [
            .. Encoded.UInt32(PcGamePlayScenario.RegistryId),
            0x00,
            0x00,
            .. Encoded.UInt32(GameScenario.RegistryId),
            0x00,
            0x00,
            .. Encoded.UInt32(0x01000000),
        ];

        Assert.Equal(PacketError.CountOutOfRange, PcGamePlayScenario.TryRead(body).Error);
    }

    [Fact]
    public void AShotPlayedBackIsTheFiringSlotThenHowItEnded()
    {
        var report = new PcGamePlayEndScenario(0x02, 0x03);
        byte[] expected = [.. Encoded.UInt32(PcGamePlayEndScenario.RegistryId), 0x02, 0x03];

        Assert.Equal(expected, Encoded.Bytes(report.Write));
        Assert.Equal(report, PcGamePlayEndScenario.TryRead(expected).Packet);

        // The slot a server-made shot names comes back as it went.
        Assert.Equal(new PcGamePlayEndScenario(0xFE, 0), PcGamePlayEndScenario.TryRead([.. Encoded.UInt32(PcGamePlayEndScenario.RegistryId), 0xFE, 0x00]).Packet);
    }

    [Fact]
    public void ATurnEndIsTheSlotTheSeedThenAFlagAndTheGaugeChangeItFlags()
    {
        var plain = new PcGamePlayTurnEnd(0x02, 0x03040506, TeamGauge: null);
        byte[] plainBody = [.. Encoded.UInt32(PcGamePlayTurnEnd.RegistryId), 0x02, .. Encoded.UInt32(0x03040506), 0x00];

        Assert.Equal(plainBody, Encoded.Bytes(plain.Write));
        Assert.Equal(plain, PcGamePlayTurnEnd.TryRead(plainBody).Packet);

        var gauged = new PcGamePlayTurnEnd(0x02, 0x03040506, TeamGauge);
        byte[] gaugedBody = [.. Encoded.UInt32(PcGamePlayTurnEnd.RegistryId), 0x02, .. Encoded.UInt32(0x03040506), 0x01, .. TeamGaugeBytes()];

        Assert.Equal(gaugedBody, Encoded.Bytes(gauged.Write));
        Assert.Equal(gauged, PcGamePlayTurnEnd.TryRead(gaugedBody).Packet);
    }

    [Fact]
    public void ATurnEndsFlagIsZeroOrOneAndWhatItFlagsIsAGaugeChange()
    {
        byte[] badFlag = [.. Encoded.UInt32(PcGamePlayTurnEnd.RegistryId), 0x02, .. Encoded.UInt32(0), 0x02, .. TeamGaugeBytes()];
        byte[] notAGauge = [.. Encoded.UInt32(PcGamePlayTurnEnd.RegistryId), 0x02, .. Encoded.UInt32(0), 0x01, .. CommonBytes(GameScenarioCommand.RegistryId, 0x05, 0x08, 0x06070809, 0x0A0B0C0D)];

        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayTurnEnd.TryRead(badFlag).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayTurnEnd.TryRead(notAGauge).Error);
    }

    [Fact]
    public void APassIsTheSlotWhereItsCharacterStandsWhichWayItFacesThenItsStats()
    {
        var pass = new PcGamePlayPassTurn(0x02, 0x03040506, -0x0708090A, 0x0B, 0x0C0D0E0F);
        byte[] expected =
        [
            .. Encoded.UInt32(PcGamePlayPassTurn.RegistryId),
            0x02,
            .. Encoded.Int32(0x03040506),
            .. Encoded.Int32(-0x0708090A),
            0x0B,
            .. Encoded.UInt32(0x0C0D0E0F),
        ];

        Assert.Equal(expected, Encoded.Bytes(pass.Write));
        Assert.Equal(pass, PcGamePlayPassTurn.TryRead(expected).Packet);
    }

    [Fact]
    public void AStepOfAWalkAndAPlaceAreEachTheSlotWhereItsCharacterIsThenWhichWayItFaces()
    {
        var move = new PcGamePlayMove(0x02, 0x03040506, -0x0708090A, 0x0B);
        byte[] moveBody = [.. Encoded.UInt32(PcGamePlayMove.RegistryId), 0x02, .. Encoded.Int32(0x03040506), .. Encoded.Int32(-0x0708090A), 0x0B];
        var place = new PcGamePlaySetPos(0x04, 0x05060708, 0x090A0B0C, 0x0D);
        byte[] placeBody = [.. Encoded.UInt32(PcGamePlaySetPos.RegistryId), 0x04, .. Encoded.Int32(0x05060708), .. Encoded.Int32(0x090A0B0C), 0x0D];

        Assert.Equal(moveBody, Encoded.Bytes(move.Write));
        Assert.Equal(move, PcGamePlayMove.TryRead(moveBody).Packet);
        Assert.Equal(placeBody, Encoded.Bytes(place.Write));
        Assert.Equal(place, PcGamePlaySetPos.TryRead(placeBody).Packet);
    }

    [Fact]
    public void AFallIsTheSlotThenWhereItsCharacterFellFrom()
    {
        var fall = new PcGamePlayDeathByDrop(0x02, -0x03040506, 0x0708090A);
        byte[] expected = [.. Encoded.UInt32(PcGamePlayDeathByDrop.RegistryId), 0x02, .. Encoded.Int32(-0x03040506), .. Encoded.Int32(0x0708090A)];

        Assert.Equal(expected, Encoded.Bytes(fall.Write));
        Assert.Equal(fall, PcGamePlayDeathByDrop.TryRead(expected).Packet);
    }

    [Fact]
    public void AGhostRequestIsItsSlotAndAGhostAddsWhereItAppearsAndItsTeam()
    {
        var request = new PqGamePlayMakeGhost(0x02);
        byte[] requestBody = [.. Encoded.UInt32(PqGamePlayMakeGhost.RegistryId), 0x02];
        var ghost = new PcGamePlayMakeGhost(0x03, 0x04050607, -0x08090A0B, 0x01);
        byte[] ghostBody = [.. Encoded.UInt32(PcGamePlayMakeGhost.RegistryId), 0x03, .. Encoded.Int32(0x04050607), .. Encoded.Int32(-0x08090A0B), 0x01];

        Assert.Equal(requestBody, Encoded.Bytes(request.Write));
        Assert.Equal(request, PqGamePlayMakeGhost.TryRead(requestBody).Packet);
        Assert.Equal(ghostBody, Encoded.Bytes(ghost.Write));
        Assert.Equal(ghost, PcGamePlayMakeGhost.TryRead(ghostBody).Packet);
    }

    [Fact]
    public void ASlotOutsideTheRoomCanBeNeitherBuiltNorReadInAnyTurnPacketNamingACharacter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayTurnEnd(8, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayPassTurn(8, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayMove(8, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlaySetPos(8, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayDeathByDrop(8, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PqGamePlayMakeGhost(8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcGamePlayMakeGhost(8, 0, 0, 0));

        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayTurnEnd.TryRead([.. Encoded.UInt32(PcGamePlayTurnEnd.RegistryId), 0x08, .. Encoded.UInt32(0), 0x00]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayPassTurn.TryRead([.. Encoded.UInt32(PcGamePlayPassTurn.RegistryId), 0x08, .. new byte[13]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayMove.TryRead([.. Encoded.UInt32(PcGamePlayMove.RegistryId), 0x08, .. new byte[9]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlaySetPos.TryRead([.. Encoded.UInt32(PcGamePlaySetPos.RegistryId), 0x08, .. new byte[9]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayDeathByDrop.TryRead([.. Encoded.UInt32(PcGamePlayDeathByDrop.RegistryId), 0x08, .. new byte[8]]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PqGamePlayMakeGhost.TryRead([.. Encoded.UInt32(PqGamePlayMakeGhost.RegistryId), 0x08]).Error);
        Assert.Equal(PacketError.UnexpectedValue, PcGamePlayMakeGhost.TryRead([.. Encoded.UInt32(PcGamePlayMakeGhost.RegistryId), 0x08, .. new byte[9]]).Error);
    }

    [Fact]
    public void ABodyCutShortOrWithBytesLeftOverIsRefused()
    {
        var shot = Encoded.Bytes(new PcGamePlayScenario(0x01, 0x02, Scenario(), 0, 0, Integrity).Write);
        var pass = Encoded.Bytes(new PcGamePlayPassTurn(0x02, 1, 2, 3, 4).Write);

        Assert.Equal(PacketError.Truncated, PcGamePlayScenario.TryRead(shot.AsSpan(0, shot.Length - 1)).Error);
        Assert.Equal(PacketError.TrailingBytes, PcGamePlayScenario.TryRead([.. shot, 0x00]).Error);
        Assert.Equal(PacketError.Truncated, PcGamePlayPassTurn.TryRead(pass.AsSpan(0, pass.Length - 1)).Error);
        Assert.Equal(PacketError.TrailingBytes, PcGamePlayPassTurn.TryRead([.. pass, 0x00]).Error);
        Assert.Equal(PacketError.Truncated, PcGamePlayEndScenario.TryRead([.. Encoded.UInt32(PcGamePlayEndScenario.RegistryId), 0x02]).Error);
    }

    // A shot with a step of every kind, the missiles' among the steps and the rest among
    // the results, and two delays and two slots reached.
    private static GameScenario Scenario() =>
        Scenario([EndMark, Normal, Weapon, Guided, Fire], [Damage, MakeItemBox, EatItemBox, TeamGauge, UseBonusGameItem]);

    private static GameScenario Scenario(IReadOnlyList<GameScenarioCommand> commands, IReadOnlyList<GameScenarioCommand> results) =>
        new(0x03, 0x04, commands, [0x1112, 0x1314], results, 0x1516, 0x1718191A, [0x01, 0x02], 0x1B1C1D1E);

    private static byte[] ScenarioBytes() =>
    [
        .. Encoded.UInt32(GameScenario.RegistryId),
        0x03,
        0x04,
        .. Encoded.UInt32(5),
        .. CommonBytes(GameScenarioCommand.RegistryId, 0x05, 0x08, 0x06070809, 0x0A0B0C0D),
        .. CommonBytes(GameScenarioCommandNormal.RegistryId, 0x11, 0x0E, 0x12131415, 0x16171819),
        .. Encoded.UInt32(0x1A1B1C1D),
        .. Encoded.UInt32(0x1E1F2021),
        .. CommonBytes(GameScenarioCommandWeapon.RegistryId, 0x21, 0x02, 0x22232425, 0x26272829),
        .. FlightBytes(0x2A, 0x2B, 0x2C2D2E2F, -0x30313233, 0x34353637, 1.5f, 0x38),
        .. CommonBytes(GameScenarioCommandGuided.RegistryId, 0x41, 0x02, 0x42434445, 0x46474849),
        .. FlightBytes(0x4A, 0x4B, 0x4C4D4E4F, 0x50515253, 0x54555657, -0.25f, 0x58),
        .. Encoded.Int32(0x595A5B5C),
        .. Encoded.Int32(-0x5D5E5F60),
        .. CommonBytes(GameScenarioCommandFire.RegistryId, 0x61, 0x00, 0x62636465, 0x66676869),
        .. FlightBytes(0x6A, 0x6B, 0x6C6D6E6F, 0x70717273, 0x74757677, 2f, 0x78),
        .. Encoded.UInt16(0x797A),
        0x7B,
        .. Encoded.Int32(0x7C7D7E7F),
        .. Encoded.Int32(-0x01020304),
        .. Encoded.Int32(0x05060708),
        .. Encoded.Int32(0x090A0B0C),
        0x0D,
        .. Encoded.UInt16(0x0E0F),
        .. Encoded.UInt32(2),
        .. Encoded.UInt16(0x1112),
        .. Encoded.UInt16(0x1314),
        .. Encoded.UInt32(5),
        .. CommonBytes(GameScenarioCommandDamage.RegistryId, 0x64, 0x04, 0x11121314, 0x15161718),
        .. Encoded.UInt32(0x191A1B1C),
        .. Encoded.UInt32(0x1D1E1F20),
        .. Encoded.UInt16(0x2122),
        .. Encoded.UInt16(0x2324),
        .. CommonBytes(GameScenarioCommandMakeItemBox.RegistryId, 0x02, 0x0A, 0x25262728, 0x292A2B2C),
        .. Encoded.UInt64(0x2D2E2F3031323334),
        .. Encoded.Int32(-0x35363738),
        .. Encoded.Int32(0x393A3B3C),
        .. CommonBytes(GameScenarioCommandEatItemBox.RegistryId, 0x03, 0x0B, 0x3D3E3F40, 0x41424344),
        .. Encoded.UInt64(0x4546474849505152),
        .. Encoded.UInt16(0x5354),
        .. Encoded.UInt32(0x55565758),
        .. TeamGaugeBytes(),
        .. CommonBytes(GameScenarioCommandUseBonusGameItem.RegistryId, 0x05, 0x0F, 0x66676869, 0x6A6B6C6D),
        .. Encoded.UInt16(0x6E6F),
        .. Encoded.UInt16(0x1516),
        .. Encoded.UInt32(0x1718191A),
        .. Encoded.UInt32(2),
        0x01,
        0x02,
        .. Encoded.UInt32(0x1B1C1D1E),
    ];

    private static byte[] TeamGaugeBytes() =>
    [
        .. CommonBytes(GameScenarioCommandTeamGauge.RegistryId, 0x04, 0x0C, 0x595A5B5C, 0x5D5E5F60),
        0x61,
        .. Encoded.UInt16(0x6263),
        .. Encoded.UInt16(0x6465),
    ];

    // The id of a step's kind, then the fields every step has.
    private static byte[] CommonBytes(uint registryId, byte slot, byte type, uint tick, uint duration) =>
        [.. Encoded.UInt32(registryId), slot, type, .. Encoded.UInt32(tick), .. Encoded.UInt32(duration)];

    // The fields of a missile's flight.
    private static byte[] FlightBytes(byte missile, byte missileType, int impactX, int impactY, uint fireStart, float multiplier, byte special) =>
    [
        missile,
        missileType,
        .. Encoded.Int32(impactX),
        .. Encoded.Int32(impactY),
        .. Encoded.UInt32(fireStart),
        .. Encoded.Single(multiplier),
        special,
    ];
}
