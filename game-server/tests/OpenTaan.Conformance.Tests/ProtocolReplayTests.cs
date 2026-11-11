using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Connection;
using OpenTaan.Protocol.GamePlay;
using OpenTaan.Protocol.Handshake;
using OpenTaan.Protocol.Lobby;
using OpenTaan.Protocol.Matches;
using OpenTaan.Protocol.PlayTime;
using OpenTaan.Protocol.Results;
using OpenTaan.Protocol.Rooms;
using OpenTaan.Transport;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Every recorded body of a packet this server implements, read by its codec and
/// written back, in both directions.
/// </summary>
/// <remarks>
/// A round trip alone cannot catch a codec that reads and writes the same field in
/// the same wrong place, so two checks here do not go through the codecs at all: the
/// registry ids are compared with the contract's table, and where the contract
/// declares a layout, the recorded bytes are read field by field from that
/// declaration and compared with what the codec made of them.
/// </remarks>
public sealed class ProtocolReplayTests
{
    // Every codec, by the id that leads its body. A codec missing from here would
    // never be replayed, so a test below fails when one is.
    private static readonly Dictionary<uint, Func<byte[], Replay>> Codecs = new()
    {
        [PcFirstAccept.RegistryId] = body => Replay.Of(PcFirstAccept.TryRead(body), packet => packet.Write),
        [PqCheckAccount.RegistryId] = body => Replay.Of(PqCheckAccount.TryRead(body), packet => packet.Write),
        [PrCheckAccount.RegistryId] = body => Replay.Of(PrCheckAccount.TryRead(body), packet => packet.Write),
        [PqCreateAccount.RegistryId] = body => Replay.Of(PqCreateAccount.TryRead(body), packet => packet.Write),
        [PrCreateAccount.RegistryId] = body => Replay.Of(PrCreateAccount.TryRead(body), packet => packet.Write),
        [PqUserLogin.RegistryId] = body => Replay.Of(PqUserLogin.TryRead(body), packet => packet.Write),
        [PrUserLogin.RegistryId] = body => Replay.Of(PrUserLogin.TryRead(body), packet => packet.Write),
        [PqExistCharacter.RegistryId] = body => Replay.Of(PqExistCharacter.TryRead(body), packet => packet.Write),
        [PrExistCharacter.RegistryId] = body => Replay.Of(PrExistCharacter.TryRead(body), packet => packet.Write),
        [PrFirstPacket.RegistryId] = body => Replay.Of(PrFirstPacket.TryRead(body), packet => packet.Write),
        [PqNewCharacter.RegistryId] = body => Replay.Of(PqNewCharacter.TryRead(body), packet => packet.Write),
        [PrNewCharacter.RegistryId] = body => Replay.Of(PrNewCharacter.TryRead(body), packet => packet.Write),
        [PqSelectCharacter.RegistryId] = body => Replay.Of(PqSelectCharacter.TryRead(body), packet => packet.Write),
        [PrSelectCharacter.RegistryId] = body => Replay.Of(PrSelectCharacter.TryRead(body), packet => packet.Write),
        [PqConfirmName.RegistryId] = body => Replay.Of(PqConfirmName.TryRead(body), packet => packet.Write),
        [PrConfirmName.RegistryId] = body => Replay.Of(PrConfirmName.TryRead(body), packet => packet.Write),
        [PqDelCharacter.RegistryId] = body => Replay.Of(PqDelCharacter.TryRead(body), packet => packet.Write),
        [PrDelCharacter.RegistryId] = body => Replay.Of(PrDelCharacter.TryRead(body), packet => packet.Write),
        [PcUpdateCash.RegistryId] = body => Replay.Of(PcUpdateCash.TryRead(body), packet => packet.Write),
        [PqGetChannelList.RegistryId] = body => Replay.Of(PqGetChannelList.TryRead(body), packet => packet.Write),
        [PrGetChannelList.RegistryId] = body => Replay.Of(PrGetChannelList.TryRead(body), packet => packet.Write),
        [PqChannelMoveTo.RegistryId] = body => Replay.Of(PqChannelMoveTo.TryRead(body), packet => packet.Write),
        [PrChannelMoveTo.RegistryId] = body => Replay.Of(PrChannelMoveTo.TryRead(body), packet => packet.Write),
        [PqLobbyEnter.RegistryId] = body => Replay.Of(PqLobbyEnter.TryRead(body), packet => packet.Write),
        [PrLobbyEnter.RegistryId] = body => Replay.Of(PrLobbyEnter.TryRead(body), packet => packet.Write),
        [PcLobbyInitRoomList.RegistryId] = body => Replay.Of(PcLobbyInitRoomList.TryRead(body), packet => packet.Write),
        [PcLobbyInitUserList.RegistryId] = body => Replay.Of(PcLobbyInitUserList.TryRead(body), packet => packet.Write),
        [PqLobbyLeave.RegistryId] = body => Replay.Of(PqLobbyLeave.TryRead(body), packet => packet.Write),
        [PrLobbyLeave.RegistryId] = body => Replay.Of(PrLobbyLeave.TryRead(body), packet => packet.Write),
        [PcUploadWebImage.RegistryId] = body => Replay.Of(PcUploadWebImage.TryRead(body), packet => packet.Write),
        [PcLobbyUpdateUserList.RegistryId] = body => Replay.Of(PcLobbyUpdateUserList.TryRead(body), packet => packet.Write),
        [PcTutorialFinished.RegistryId] = body => Replay.Of(PcTutorialFinished.TryRead(body), packet => packet.Write),
        [PcGamePlayNotifyEatItem.RegistryId] = body => Replay.Of(PcGamePlayNotifyEatItem.TryRead(body), packet => packet.Write),
        [PcGamePlaySetGhostPos.RegistryId] = body => Replay.Of(PcGamePlaySetGhostPos.TryRead(body), packet => packet.Write),
        [PcNotifyPlayableTime.RegistryId] = body => Replay.Of(PcNotifyPlayableTime.TryRead(body), packet => packet.Write),
        [PcPlayableTimeCtrl.RegistryId] = body => Replay.Of(PcPlayableTimeCtrl.TryRead(body), packet => packet.Write),
        [PcResetPlayableTime.RegistryId] = body => Replay.Of(PcResetPlayableTime.TryRead(body), packet => packet.Write),
        [PqGameSessionCreate.RegistryId] = body => Replay.Of(PqGameSessionCreate.TryRead(body), packet => packet.Write),
        [PqGameSessionEnter.RegistryId] = body => Replay.Of(PqGameSessionEnter.TryRead(body), packet => packet.Write),
        [PqGameSessionQuickJoin.RegistryId] = body => Replay.Of(PqGameSessionQuickJoin.TryRead(body), packet => packet.Write),
        [PqGameSessionLeave.RegistryId] = body => Replay.Of(PqGameSessionLeave.TryRead(body), packet => packet.Write),
        [PcGameSessionInit.RegistryId] = body => Replay.Of(PcGameSessionInit.TryRead(body), packet => packet.Write),
        [PrGameSessionCreateFail.RegistryId] = body => Replay.Of(PrGameSessionCreateFail.TryRead(body), packet => packet.Write),
        [PrGameSessionEnterFail.RegistryId] = body => Replay.Of(PrGameSessionEnterFail.TryRead(body), packet => packet.Write),
        [PrGameSessionQuickJoinFail.RegistryId] = body => Replay.Of(PrGameSessionQuickJoinFail.TryRead(body), packet => packet.Write),
        [PrGameSessionLeave.RegistryId] = body => Replay.Of(PrGameSessionLeave.TryRead(body), packet => packet.Write),
        [PcGameSessionUpdateSlot.RegistryId] = body => Replay.Of(PcGameSessionUpdateSlot.TryRead(body), packet => packet.Write),
        [PcGameSessionUpdateOption.RegistryId] = body => Replay.Of(PcGameSessionUpdateOption.TryRead(body), packet => packet.Write),
        [PcGameSessionReady.RegistryId] = body => Replay.Of(PcGameSessionReady.TryRead(body), packet => packet.Write),
        [PqGameSessionReady.RegistryId] = body => Replay.Of(PqGameSessionReady.TryRead(body), packet => packet.Write),
        [PqGameSessionChangeTeam.RegistryId] = body => Replay.Of(PqGameSessionChangeTeam.TryRead(body), packet => packet.Write),
        [PcGameSessionChangeTeam.RegistryId] = body => Replay.Of(PcGameSessionChangeTeam.TryRead(body), packet => packet.Write),
        [PqGameSessionChangeWeapon.RegistryId] = body => Replay.Of(PqGameSessionChangeWeapon.TryRead(body), packet => packet.Write),
        [PcGameSessionChangeWeapon.RegistryId] = body => Replay.Of(PcGameSessionChangeWeapon.TryRead(body), packet => packet.Write),
        [PqGameSessionChangeGameOption.RegistryId] = body => Replay.Of(PqGameSessionChangeGameOption.TryRead(body), packet => packet.Write),
        [PcGameSessionChangeGameOption.RegistryId] = body => Replay.Of(PcGameSessionChangeGameOption.TryRead(body), packet => packet.Write),
        [PqGameSessionOpenCloseSlot.RegistryId] = body => Replay.Of(PqGameSessionOpenCloseSlot.TryRead(body), packet => packet.Write),
        [PcGameSessionOpenCloseSlot.RegistryId] = body => Replay.Of(PcGameSessionOpenCloseSlot.TryRead(body), packet => packet.Write),
        [PqGameSessionUserExpel.RegistryId] = body => Replay.Of(PqGameSessionUserExpel.TryRead(body), packet => packet.Write),
        [PcGameSessionUserExpel.RegistryId] = body => Replay.Of(PcGameSessionUserExpel.TryRead(body), packet => packet.Write),
        [PcLobbyUpdateRoomList.RegistryId] = body => Replay.Of(PcLobbyUpdateRoomList.TryRead(body), packet => packet.Write),
        [PqLobbyViewRoomSimpleInfo.RegistryId] = body => Replay.Of(PqLobbyViewRoomSimpleInfo.TryRead(body), packet => packet.Write),
        [PrLobbyViewRoomSimpleInfo.RegistryId] = body => Replay.Of(PrLobbyViewRoomSimpleInfo.TryRead(body), packet => packet.Write),
        [PqGameSessionStartGame.RegistryId] = body => Replay.Of(PqGameSessionStartGame.TryRead(body), packet => packet.Write),
        [PcGameSessionStartGame.RegistryId] = body => Replay.Of(PcGameSessionStartGame.TryRead(body), packet => packet.Write),
        [PqGameSessionReportEnemies.RegistryId] = body => Replay.Of(PqGameSessionReportEnemies.TryRead(body), packet => packet.Write),
        [PcGamePlayLoadingDone.RegistryId] = body => Replay.Of(PcGamePlayLoadingDone.TryRead(body), packet => packet.Write),
        [PqGameSessionStartMissionLevel.RegistryId] = body => Replay.Of(PqGameSessionStartMissionLevel.TryRead(body), packet => packet.Write),
        [PcGameSessionStartMissionLevel.RegistryId] = body => Replay.Of(PcGameSessionStartMissionLevel.TryRead(body), packet => packet.Write),
        [PqGameSessionMissionLevelReady.RegistryId] = body => Replay.Of(PqGameSessionMissionLevelReady.TryRead(body), packet => packet.Write),
        [PcGamePlayTurnStart.RegistryId] = body => Replay.Of(PcGamePlayTurnStart.TryRead(body), packet => packet.Write),
        [PcGamePlayGiveUp.RegistryId] = body => Replay.Of(PcGamePlayGiveUp.TryRead(body), packet => packet.Write),
        [PcGamePlayLeaveUser.RegistryId] = body => Replay.Of(PcGamePlayLeaveUser.TryRead(body), packet => packet.Write),
        [PcGameSessionEndGame.RegistryId] = body => Replay.Of(PcGameSessionEndGame.TryRead(body), packet => packet.Write),
        [PcResultSetReward.RegistryId] = body => Replay.Of(PcResultSetReward.TryRead(body), packet => packet.Write),
        [PqResultRandSelect.RegistryId] = body => Replay.Of(PqResultRandSelect.TryRead(body), packet => packet.Write),
        [PqResultSelectCard.RegistryId] = body => Replay.Of(PqResultSelectCard.TryRead(body), packet => packet.Write),
        [PrResultSelectCard.RegistryId] = body => Replay.Of(PrResultSelectCard.TryRead(body), packet => packet.Write),
        [PrResultAlreadyOpend.RegistryId] = body => Replay.Of(PrResultAlreadyOpend.TryRead(body), packet => packet.Write),
    };

    // A room and an integrity report from invented values whose bytes differ field to
    // field, for the bodies below.
    private static readonly GameSessionInfo InventedRoom = new(
        0x0A0B, RoomStatus.Playing, 0x03, "Phòng", GameType.TeamGame, GameMode.CaptainItems, ChanceMode.Bonus, 0x05, 0x06, "mk", 0x0C0D);

    private static readonly IntegrityReport InventedIntegrity = new(0x11223344, 0x55667788, 0x99, 0xAABBCCDD, 0x0E0F1011, 0x12131415);

    // Codecs for packets no recorded session carries, built from the client's own reader.
    // Each has a body written from invented values, whose bytes differ field to field, so
    // that the declared layout still checks where the codec puts each field.
    private static readonly Dictionary<uint, byte[]> Unrecorded = new()
    {
        [PrFirstPacket.RegistryId] = Written(new PrFirstPacket(0x11, 0x44332211, 0x01)),
        [PcUploadWebImage.RegistryId] = Written(new PcUploadWebImage(0x44332211, [0x55, 0x66, 0x77])),
        [PcLobbyUpdateUserList.RegistryId] = Written(PcLobbyUpdateUserList.Removing([0x44332211, 0x88776655])),
        [PrGameSessionCreateFail.RegistryId] = Written(new PrGameSessionCreateFail(CreateRefusal.Refused)),
        [PrGameSessionEnterFail.RegistryId] = Written(new PrGameSessionEnterFail(EnterRefusal.WrongPassword)),
        [PrGameSessionQuickJoinFail.RegistryId] = Written(new PrGameSessionQuickJoinFail()),
        [PcGameSessionUpdateOption.RegistryId] = Written(new PcGameSessionUpdateOption(new GameSessionOption(0x44332211, 0x88776655))),
        [PcLobbyUpdateRoomList.RegistryId] = Written(new PcLobbyUpdateRoomList(
            [GameSessionInfoPack.Listing(InventedRoom), GameSessionInfoPack.Removal(0x1112), GameSessionInfoPack.Listing(InventedRoom with { Password = null })])),
        [PcGamePlayLeaveUser.RegistryId] = Written(new PcGamePlayLeaveUser(0x05)),
    };

    // Recorded packets in shapes no recording shows, written from invented values so that
    // the declared layout checks those shapes too: every recorded room list is empty, every
    // recorded integrity report is zeros, and every recorded team change moves one
    // character, so no swap inside one could show.
    private static readonly byte[][] UnrecordedShapes =
    [
        Written(new PcLobbyInitRoomList([GameSessionInfoPack.Listing(InventedRoom), GameSessionInfoPack.Listing(InventedRoom with { RoomNumber = 0x1112, Password = null })])),
        Written(new PqGameSessionCreate(InventedRoom, InventedIntegrity)),
        Written(new PqGameSessionEnter(0x0A0B, "mk", InventedIntegrity)),
        Written(new PqGameSessionQuickJoin(PqGameSessionQuickJoin.AnyRoom, InventedIntegrity)),
        Written(new PcGameSessionChangeTeam(0x44332211, 2, 0, 1, 6, 7)),

        // Every recorded match start and turn start carries the same few values, most of
        // them 0, every list in a turn start is empty, and no recorded result row has a
        // bonus or a medal.
        Written(new PcGameSessionStartGame(
            0x0A0B,
            [
                new GameSlotPlayInfo(0x01, 0x02030405, -0x06070809, true, 0x0A0B0C0D, 0x0E0F1011, 0x12131415, 0x1617, 0x1819, 0x1A1B, 0x1C1D),
                new GameSlotPlayInfo(0x06, 0x22232425, 0x26272829, false, 0x2A2B2C2D, 0x2E2F3031, 0x32333435, 0x3637, 0x3839, 0x3A3B, 0x3C3D),
            ],
            0x3E3F4041,
            0x4243,
            [0x07, 0x05, 0x02],
            0x44454647)),
        Written(new PcGamePlayTurnStart(
            0x06,
            0x07,
            0x08090A0B,
            0x0C0D,
            0x0E,
            [0x0F10111213141516, 0x1718191A1B1C1D1E],
            [new GhostMove(0x02, 0x1F202122, -0x23242526, 0x27282930, 0x31323334)],
            [new HpUpdate(0x03, 0x35, -0x36373839), new HpUpdate(0x04, 0x3A, 0x3B3C3D3E)],
            0x3F404142,
            [0x43444546, 0x47484950, 0x51525354],
            0x5556)),
        Written(new PcGamePlayLoadingDone([new LoadedCharacter(0x04, 0x01020304, -0x05060708), new LoadedCharacter(0x05, 0x090A0B0C, 0x0D0E0F10)])),
        Written(new PqGameSessionStartMissionLevel(0x03, 0x04050607, 0x08090A0B, true)),
        Written(new PcGameSessionEndGame(
            new GameResult(
                2,
                [
                    new GameResultSlot(0x00, 0x0102, -0x03040506, [new ExpResult(0x0708, 0x090A0B0C, 0x0D0E0F10, 0x11121314)], 0x15161718, 0x19, 0x1A1B1C1D, 0.25f, 0x1E1F2021,
                        [new ResultBonus(0x22, 0x23242526, 0x2728292A), new ResultBonus(0x2B, 0x2C2D2E2F, 0x30313233)], 0x34, 0x35, 0x36, true, 0x37383940, 0x41424344),
                    .. Enumerable.Range(1, 7).Select(slot => GameResultSlot.Empty((byte)slot)),
                ],
                -0x45464748,
                0x494A4B4C),
            0x4D)),
        Written(new PcResultSetReward(
        [
            new GameRewardGold("Vàng", 0x01020304, 0x05060708, true, 0x090A0B0C),
            new GameRewardExp("Kinh nghiệm", 0x0D0E0F10, 0x11121314),
            .. Enumerable.Range(0, 6).Select(_ => new GameRewardGold("Empty", 0, 0, false, 0)),
        ])),
    ];

    [Fact]
    public void TheComputedIdOfEveryNameInTheRegistryIsTheIdItRecords()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var wrong = WireContract.Registry.Ids
            .Where(entry => RegistryIds.Of(entry.Key) != entry.Value)
            .Select(entry => entry.Key)
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryImplementedClassCarriesTheIdTheRegistryGivesIt()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var wrong = ImplementedClasses()
            .Where(implemented => !WireContract.Registry.Ids.TryGetValue(implemented.WireName, out var id)
                                  || id != implemented.RegistryId)
            .Select(implemented => implemented.WireName)
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryPacketCodecIsReplayed()
    {
        var unreplayed = ImplementedClasses()
            .Where(implemented => implemented.IsPacket && !Codecs.ContainsKey(implemented.RegistryId))
            .Select(implemented => implemented.WireName)
            .ToArray();

        Assert.Empty(unreplayed);
    }

    [Fact]
    public void EveryRecordedBodyOfAnImplementedPacketReadsAndWritesBackUnchanged()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var failures = new List<string>();
        var exercised = new HashSet<uint>();

        foreach (var (origin, body) in Bodies())
        {
            var id = LeadingId(body);
            if (!Codecs.TryGetValue(id, out var replay))
            {
                continue;
            }

            exercised.Add(id);
            var result = replay(body);

            if (result.Error != PacketError.None)
            {
                failures.Add($"{origin}: {NameOf(id)} refused with {result.Error}");
            }
            else if (!result.Written.AsSpan().SequenceEqual(body))
            {
                failures.Add($"{origin}: {NameOf(id)} wrote back different bytes");
            }
        }

        Assert.Empty(failures);

        var neverSeen = Codecs.Keys.Where(id => !exercised.Contains(id) && !Unrecorded.ContainsKey(id)).Select(NameOf).ToArray();
        Assert.True(neverSeen.Length == 0, "no recorded body to check: " + string.Join(", ", neverSeen));

        // Once a session records one, it is checked like the rest and leaves the list.
        var recorded = Unrecorded.Keys.Where(exercised.Contains).Select(NameOf).ToArray();
        Assert.True(recorded.Length == 0, "recorded after all, so no longer unrecorded: " + string.Join(", ", recorded));
    }

    [Fact]
    public void DeclaredLayoutsAgreeWithWhatTheCodecsRead()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        // Every class, for the objects a packet holds, which are read by their own class's
        // layout; the packets checked are those whose own fields are declared.
        var classes = WireContract.Spec.Classes.ToDictionary(spec => spec.RegistryId);

        var failures = new List<string>();
        var written = Unrecorded.Values.Concat(UnrecordedShapes).Select(body => ("a written body", body));

        foreach (var (origin, body) in Bodies().Concat(written))
        {
            var id = LeadingId(body);
            if (!Codecs.ContainsKey(id) || classes.GetValueOrDefault(id) is not { } spec
                || !(spec.Fields.Count > 0 || DeclaredLayout.HasClientLayout(spec.WireName)))
            {
                continue;
            }

            failures.AddRange(DeclaredLayout.Mismatches(body, Decode(id, body), classes)
                .Select(mismatch => $"{origin}: {mismatch}"));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryRecordedHandshakeCarriesThatConnectionsKeys()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        // A session can hold several connections, a reconnection for one, each with
        // its own keys. Each greeting is checked against the keys of its own exchange.
        var handshakes = WireContract.Sessions
            .SelectMany(session => session.Exchanges
                .Where(exchange => exchange.Crypto is not null)
                .Select(exchange => (session.Origin, Keys: exchange.Crypto!, exchange.Frames)))
            .ToArray();

        Assert.NotEmpty(handshakes);

        foreach (var (origin, keys, frames) in handshakes)
        {
            var greeting = Assert.Single(frames, frame => LeadingId(frame.Body()) == PcFirstAccept.RegistryId);
            Assert.False(greeting.Encrypted, $"{origin}: the greeting must travel unencrypted");

            var read = PcFirstAccept.TryRead(greeting.Body());
            Assert.True(read.Succeeded, $"{origin}: {read.Error}");
            Assert.Equal(new PcFirstAccept(keys.FirstSeed, keys.SecondSeed, keys.Shift), read.Packet);

            // The values must be ones this server would also accept: never the seed
            // that switches encryption off.
            var accepted = new HandshakeKeys(read.Packet.Seed1, read.Packet.Seed2, read.Packet.Shift);
            Assert.NotEqual(OpenTaanCipher.PlaintextSeed, accepted.BaseSeed);
        }
    }

    private static IEnumerable<(string Origin, byte[] Body)> Bodies()
    {
        foreach (var session in WireContract.Sessions)
        {
            foreach (var exchange in session.Exchanges)
            {
                if (exchange.Inbound is { } inbound)
                {
                    yield return (session.Origin, Convert.FromHexString(inbound));
                }

                foreach (var frame in exchange.Frames)
                {
                    yield return (session.Origin, frame.Body());
                }
            }
        }
    }

    // The packets whose layout the contract declares, decoded for comparison with it.
    private static readonly Dictionary<uint, Func<byte[], object?>> Decoders = new()
    {
        [PcFirstAccept.RegistryId] = body => PcFirstAccept.TryRead(body).Packet,
        [PcTutorialFinished.RegistryId] = body => PcTutorialFinished.TryRead(body).Packet,
        [PcGamePlayNotifyEatItem.RegistryId] = body => PcGamePlayNotifyEatItem.TryRead(body).Packet,
        [PcGamePlaySetGhostPos.RegistryId] = body => PcGamePlaySetGhostPos.TryRead(body).Packet,
        [PcNotifyPlayableTime.RegistryId] = body => PcNotifyPlayableTime.TryRead(body).Packet,
        [PcPlayableTimeCtrl.RegistryId] = body => PcPlayableTimeCtrl.TryRead(body).Packet,
        [PcResetPlayableTime.RegistryId] = body => PcResetPlayableTime.TryRead(body).Packet,
        [PrCheckAccount.RegistryId] = body => PrCheckAccount.TryRead(body).Packet,
        [PrCreateAccount.RegistryId] = body => PrCreateAccount.TryRead(body).Packet,
        [PrExistCharacter.RegistryId] = body => PrExistCharacter.TryRead(body).Packet,
        [PrFirstPacket.RegistryId] = body => PrFirstPacket.TryRead(body).Packet,
        [PrConfirmName.RegistryId] = body => PrConfirmName.TryRead(body).Packet,
        [PrSelectCharacter.RegistryId] = body => PrSelectCharacter.TryRead(body).Packet,
        [PcUpdateCash.RegistryId] = body => PcUpdateCash.TryRead(body).Packet,
        [PrLobbyEnter.RegistryId] = body => PrLobbyEnter.TryRead(body).Packet,
        [PcLobbyInitRoomList.RegistryId] = body => PcLobbyInitRoomList.TryRead(body).Packet,
        [PcLobbyInitUserList.RegistryId] = body => PcLobbyInitUserList.TryRead(body).Packet,
        [PrLobbyLeave.RegistryId] = body => PrLobbyLeave.TryRead(body).Packet,
        [PqGameSessionCreate.RegistryId] = body => PqGameSessionCreate.TryRead(body).Packet,
        [PqGameSessionEnter.RegistryId] = body => PqGameSessionEnter.TryRead(body).Packet,
        [PqGameSessionQuickJoin.RegistryId] = body => PqGameSessionQuickJoin.TryRead(body).Packet,
        [PcGameSessionInit.RegistryId] = body => PcGameSessionInit.TryRead(body).Packet,
        [PrGameSessionCreateFail.RegistryId] = body => PrGameSessionCreateFail.TryRead(body).Packet,
        [PrGameSessionEnterFail.RegistryId] = body => PrGameSessionEnterFail.TryRead(body).Packet,
        [PrGameSessionQuickJoinFail.RegistryId] = body => PrGameSessionQuickJoinFail.TryRead(body).Packet,
        [PrGameSessionLeave.RegistryId] = body => PrGameSessionLeave.TryRead(body).Packet,
        [PcGameSessionUpdateSlot.RegistryId] = body => PcGameSessionUpdateSlot.TryRead(body).Packet,
        [PcGameSessionUpdateOption.RegistryId] = body => PcGameSessionUpdateOption.TryRead(body).Packet,
        [PcGameSessionReady.RegistryId] = body => PcGameSessionReady.TryRead(body).Packet,
        [PqGameSessionUserExpel.RegistryId] = body => PqGameSessionUserExpel.TryRead(body).Packet,
        [PcGameSessionChangeTeam.RegistryId] = body => PcGameSessionChangeTeam.TryRead(body).Packet,
        [PcGameSessionChangeWeapon.RegistryId] = body => PcGameSessionChangeWeapon.TryRead(body).Packet,
        [PcGameSessionChangeGameOption.RegistryId] = body => PcGameSessionChangeGameOption.TryRead(body).Packet,
        [PcGameSessionOpenCloseSlot.RegistryId] = body => PcGameSessionOpenCloseSlot.TryRead(body).Packet,
        [PcGameSessionUserExpel.RegistryId] = body => PcGameSessionUserExpel.TryRead(body).Packet,
        [PcLobbyUpdateRoomList.RegistryId] = body => PcLobbyUpdateRoomList.TryRead(body).Packet,
        [PrLobbyViewRoomSimpleInfo.RegistryId] = body => PrLobbyViewRoomSimpleInfo.TryRead(body).Packet,
        [PqGameSessionStartGame.RegistryId] = body => PqGameSessionStartGame.TryRead(body).Packet,
        [PcGameSessionStartGame.RegistryId] = body => PcGameSessionStartGame.TryRead(body).Packet,
        [PqGameSessionReportEnemies.RegistryId] = body => PqGameSessionReportEnemies.TryRead(body).Packet,
        [PcGamePlayLoadingDone.RegistryId] = body => PcGamePlayLoadingDone.TryRead(body).Packet,
        [PqGameSessionStartMissionLevel.RegistryId] = body => PqGameSessionStartMissionLevel.TryRead(body).Packet,
        [PcGameSessionStartMissionLevel.RegistryId] = body => PcGameSessionStartMissionLevel.TryRead(body).Packet,
        [PqGameSessionMissionLevelReady.RegistryId] = body => PqGameSessionMissionLevelReady.TryRead(body).Packet,
        [PcGamePlayTurnStart.RegistryId] = body => PcGamePlayTurnStart.TryRead(body).Packet,
        [PcGamePlayGiveUp.RegistryId] = body => PcGamePlayGiveUp.TryRead(body).Packet,
        [PcGamePlayLeaveUser.RegistryId] = body => PcGamePlayLeaveUser.TryRead(body).Packet,
        [PcGameSessionEndGame.RegistryId] = body => PcGameSessionEndGame.TryRead(body).Packet,
        [PcResultSetReward.RegistryId] = body => PcResultSetReward.TryRead(body).Packet,
        [PqResultRandSelect.RegistryId] = body => PqResultRandSelect.TryRead(body).Packet,
        [PqResultSelectCard.RegistryId] = body => PqResultSelectCard.TryRead(body).Packet,
        [PrResultSelectCard.RegistryId] = body => PrResultSelectCard.TryRead(body).Packet,
        [PrResultAlreadyOpend.RegistryId] = body => PrResultAlreadyOpend.TryRead(body).Packet,
    };

    private static object Decode(uint registryId, byte[] body) =>
        Decoders.TryGetValue(registryId, out var decode)
            ? decode(body) ?? throw new InvalidOperationException($"{NameOf(registryId)} refused a recorded body")
            : throw new InvalidOperationException(
                $"{NameOf(registryId)} has a declared layout; add it to {nameof(Decoders)} so the layout is checked");

    private static IEnumerable<(string WireName, uint RegistryId, bool IsPacket)> ImplementedClasses() =>
        from type in typeof(ProtocolAssemblyMarker).Assembly.GetExportedTypes()
        let wireClass = type.GetCustomAttribute<WireClassAttribute>(inherit: false)
        where wireClass is not null
        select (wireClass.Name, RegistryIds.Of(wireClass.Name), type.GetMethod("TryRead") is not null);

    private static string NameOf(uint registryId) =>
        WireContract.Spec.Classes.FirstOrDefault(spec => spec.RegistryId == registryId)?.WireName
        ?? registryId.ToString("X8", CultureInfo.InvariantCulture);

    private static byte[] Written(IPacket packet)
    {
        var destination = new ArrayBufferWriter<byte>();
        packet.Write(destination);
        return destination.WrittenSpan.ToArray();
    }

    private static uint LeadingId(byte[] body) =>
        body.Length >= sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(body) : 0;

    private sealed record Replay(PacketError Error, byte[]? Written)
    {
        public static Replay Of<TPacket>(PacketReadResult<TPacket> result, Func<TPacket, Action<IBufferWriter<byte>>> write)
            where TPacket : class
        {
            if (!result.Succeeded)
            {
                return new Replay(result.Error, null);
            }

            var destination = new ArrayBufferWriter<byte>();
            write(result.Packet)(destination);
            return new Replay(PacketError.None, destination.WrittenSpan.ToArray());
        }
    }
}
