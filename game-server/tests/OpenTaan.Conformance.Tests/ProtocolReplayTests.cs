using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Protocol.Handshake;
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

        var neverSeen = Codecs.Keys.Where(id => !exercised.Contains(id)).Select(NameOf).ToArray();
        Assert.True(neverSeen.Length == 0, "no recorded body to check: " + string.Join(", ", neverSeen));
    }

    [Fact]
    public void DeclaredLayoutsAgreeWithWhatTheCodecsRead()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var declared = WireContract.Spec.Classes
            .Where(spec => spec.Fields.Count > 0 && Codecs.ContainsKey(spec.RegistryId))
            .ToDictionary(spec => spec.RegistryId);

        var failures = new List<string>();

        foreach (var (origin, body) in Bodies())
        {
            if (!declared.TryGetValue(LeadingId(body), out var spec))
            {
                continue;
            }

            var packet = Decode(spec.RegistryId, body);

            foreach (var (field, value) in ReadDeclared(spec, body))
            {
                var property = packet.GetType().GetProperty(PascalCase(field))
                    ?? throw new InvalidOperationException($"{spec.WireName} has no property for '{field}'");

                if (AsNumber(property.GetValue(packet)) != value)
                {
                    failures.Add($"{origin}: {spec.WireName}.{property.Name} is not what the layout puts there");
                }
            }
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
        [PrCheckAccount.RegistryId] = body => PrCheckAccount.TryRead(body).Packet,
        [PrCreateAccount.RegistryId] = body => PrCreateAccount.TryRead(body).Packet,
        [PrExistCharacter.RegistryId] = body => PrExistCharacter.TryRead(body).Packet,
    };

    private static object Decode(uint registryId, byte[] body) =>
        Decoders.TryGetValue(registryId, out var decode)
            ? decode(body) ?? throw new InvalidOperationException($"{NameOf(registryId)} refused a recorded body")
            : throw new InvalidOperationException(
                $"{NameOf(registryId)} has a declared layout; add it to {nameof(Decoders)} so the layout is checked");

    // Reads the fixed-width fields the contract declares, in its order, independently
    // of any codec. A declared type this does not know fails the test rather than
    // being skipped, so a new layout cannot go unchecked.
    private static IEnumerable<(string Field, ulong Value)> ReadDeclared(ClassSpec spec, byte[] body)
    {
        var offset = 0;
        foreach (var field in spec.Fields)
        {
            var width = field.Type switch
            {
                "u8" => 1,
                "u16" => 2,
                "u32" or "i32" => 4,
                "u64" => 8,
                _ => throw new InvalidOperationException($"{spec.WireName}.{field.Name}: '{field.Type}' is not read here yet"),
            };

            var bytes = body.AsSpan(offset, width);
            var value = width switch
            {
                1 => bytes[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            };
            offset += width;

            if (field.Name is { } name && name != "registry_id")
            {
                yield return (name, value);
            }
        }
    }

    private static ulong AsNumber(object? value) => value switch
    {
        bool flag => flag ? 1ul : 0ul,
        Enum member => Convert.ToUInt64(member, CultureInfo.InvariantCulture),
        _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
    };

    private static string PascalCase(string snakeCase) =>
        string.Concat(snakeCase.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static IEnumerable<(string WireName, uint RegistryId, bool IsPacket)> ImplementedClasses() =>
        from type in typeof(ProtocolAssemblyMarker).Assembly.GetExportedTypes()
        let wireClass = type.GetCustomAttribute<WireClassAttribute>(inherit: false)
        where wireClass is not null
        select (wireClass.Name, RegistryIds.Of(wireClass.Name), type.GetMethod("TryRead") is not null);

    private static string NameOf(uint registryId) =>
        WireContract.Spec.Classes.FirstOrDefault(spec => spec.RegistryId == registryId)?.WireName
        ?? registryId.ToString("X8", CultureInfo.InvariantCulture);

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
