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
using OpenTaan.Protocol.PlayTime;
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
    };

    // Codecs for packets no recorded session carries, built from the client's own reader.
    // Each has a body written from invented values, whose bytes differ field to field, so
    // that the declared layout still checks where the codec puts each field.
    private static readonly Dictionary<uint, byte[]> Unrecorded = new()
    {
        [PrFirstPacket.RegistryId] = Written(new PrFirstPacket(0x11, 0x44332211, 0x01)),
        [PcUploadWebImage.RegistryId] = Written(new PcUploadWebImage(0x44332211, [0x55, 0x66, 0x77])),
        [PcLobbyUpdateUserList.RegistryId] = Written(PcLobbyUpdateUserList.Removing([0x44332211, 0x88776655])),
    };

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

        // Every declared layout, nested ones included: a packet's lists hold objects that
        // are read by their own class's layout.
        var declared = WireContract.Spec.Classes
            .Where(spec => spec.Fields.Count > 0)
            .ToDictionary(spec => spec.RegistryId);

        var failures = new List<string>();

        foreach (var (origin, body) in Bodies().Concat(Unrecorded.Values.Select(body => ("a written body", body))))
        {
            var id = LeadingId(body);
            if (!Codecs.ContainsKey(id) || !declared.ContainsKey(id))
            {
                continue;
            }

            failures.AddRange(DeclaredLayout.Mismatches(body, Decode(id, body), declared)
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
