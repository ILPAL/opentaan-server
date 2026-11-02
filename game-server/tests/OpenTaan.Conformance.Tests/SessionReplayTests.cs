using System.Buffers;
using System.Buffers.Binary;
using OpenTaan.GameServer.Characters;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Characters;
using OpenTaan.Tests.GameServer;
using OpenTaan.Transport;

namespace OpenTaan.Tests.Conformance;

/// <summary>
/// Every recorded session, played by its participants against real sessions over
/// loopback sockets, for as long as the server handles what they send.
/// </summary>
/// <remarks>
/// <para>
/// Each participant gets a connection of its own, greeted with the handshake values the
/// session recorded, so the greeting itself is compared byte for byte along with every
/// reply. The recorded bodies are sent as they are; the frames around them are made
/// with this run's seeds, which the transport replay checks separately.
/// </para>
/// <para>
/// A participant is followed until it sends a packet this server does not handle yet,
/// or one whose answer goes to someone else: both belong to later stages, so the
/// participant's remaining steps are not played. Accounts are shared by everyone in the
/// session, as they are in the server.
/// </para>
/// <para>
/// One kind of step is skipped rather than played: a login the recording refused
/// because too many had failed. The login throttle arrives with the database, so this
/// server would answer that step on its merits. The steps after it are still played,
/// and still have to match.
/// </para>
/// <para>
/// One recorded value is adjusted before comparing, and only that one: a new
/// character's appearance carries the account's gender, where the recordings carry 0,
/// a placeholder of the server they were recorded from. The client draws a character's
/// default parts by that gender. The recorded answer is decoded, that field set, and
/// the answer encoded again, so every other byte is still compared as recorded.
/// </para>
/// </remarks>
public sealed class SessionReplayTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // The starting values the sessions were recorded with: placeholders, not a design.
    private static readonly CharacterOptions RecordedCharacterOptions = new()
    {
        CreatorParts = SessionHarness.ShippedCharacterOptions().CreatorParts,
        Start = new CharacterStart
        {
            ExperienceCeiling = 300,
            Gold = 1_000_000,
            RankPlacement = 1,
            RankScore = 1,
            LeagueMoney = 1,
            Wins = 1,
            Losses = 1,
            ForceMedals = 1,
            KillMedals = 1,
            SupportMedals = 1,
        },
    };

    [Fact]
    public async Task EveryParticipantIsAnsweredAsRecordedUntilItLeavesThisStage()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var failures = new List<string>();
        var compared = 0;

        foreach (var session in WireContract.Sessions)
        {
            compared += await ReplayAsync(session, failures);
        }

        Assert.Empty(failures);
        Assert.True(compared > 0, "the corpus was present but no step of it was in this stage");
    }

    private static async Task<int> ReplayAsync(Session session, List<string> failures)
    {
        CryptoParameters? nextHandshake = null;
        var services = SessionHarness.Services(
            requireRegistration: session.World?.Authentication ?? false,
            gameMasters: session.World?.GmAccountEmails,
            handshakeKeys: () => new HandshakeKeys(nextHandshake!.FirstSeed, nextHandshake.SecondSeed, nextHandshake.Shift),
            characterOptions: RecordedCharacterOptions);

        var connections = new Dictionary<string, Connection>(StringComparer.Ordinal);
        var compared = 0;

        try
        {
            for (var step = 0; step < session.Exchanges.Count; step++)
            {
                var exchange = session.Exchanges[step];
                var participant = exchange.From ?? string.Empty;
                var where = $"{session.Origin} step {step}";

                if (exchange.Event == "connect")
                {
                    await Retire(connections, participant);

                    // A connection with no handshake of its own is not a game client's.
                    nextHandshake = HandshakeFor(session, step, participant);
                    if (nextHandshake is not null)
                    {
                        connections[participant] = await SessionHarness.ConnectAsync(services, Cancel);
                    }

                    continue;
                }

                if (!connections.TryGetValue(participant, out var connection))
                {
                    continue;
                }

                if (exchange.Event == "handshake")
                {
                    Compare(failures, where, exchange.Frames[0].Body(), connection.Client.GreetingBody);
                    compared++;
                    continue;
                }

                if (exchange.Inbound is not { } inbound)
                {
                    continue;
                }

                var body = Convert.FromHexString(inbound);
                if (!services.Dispatcher.Routes(BinaryPrimitives.ReadUInt32LittleEndian(body))
                    || exchange.Frames.Any(frame => frame.To != participant))
                {
                    await Retire(connections, participant);
                    continue;
                }

                if (IsThrottleRefusal(exchange))
                {
                    continue;
                }

                await connection.Client.SendBodyAsync(body);
                foreach (var frame in exchange.Frames)
                {
                    Compare(failures, where, Adjusted(frame.Body()), await connection.Client.ReceiveBodyAsync());
                    compared++;
                }
            }
        }
        finally
        {
            foreach (var participant in connections.Keys.ToArray())
            {
                await Retire(connections, participant);
            }
        }

        return compared;
    }

    // The handshake recorded for this connection: the participant's next one, before
    // any later connection of theirs.
    private static CryptoParameters? HandshakeFor(Session session, int connect, string participant) =>
        session.Exchanges
            .Skip(connect + 1)
            .Where(exchange => (exchange.From ?? string.Empty) == participant)
            .TakeWhile(exchange => exchange.Event != "connect")
            .Select(exchange => exchange.Crypto)
            .FirstOrDefault(crypto => crypto is not null);

    // The recorded answer with each new character's appearance given its account's
    // gender, which the character's own description carries. See the remarks above.
    private static byte[] Adjusted(byte[] recorded)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(recorded) != PrNewCharacter.RegistryId
            || PrNewCharacter.TryRead(recorded).Packet is not { Catalog: { } catalog } answer)
        {
            return recorded;
        }

        var appearances = catalog.Appearances
            .Select((appearance, position) => appearance with { Gender = catalog.Characters[position].Gender })
            .ToArray();
        var adjusted = new ArrayBufferWriter<byte>();
        (answer with { Catalog = new CharacterCatalog(catalog.Characters, appearances) }).Write(adjusted);
        return adjusted.WrittenSpan.ToArray();
    }

    private static bool IsThrottleRefusal(Exchange exchange) =>
        exchange.Frames.Count == 1
        && PrUserLogin.TryRead(exchange.Frames[0].Body()).Packet is PrUserLoginFailed { Reason: LoginRefusalReason.Penalty };

    private static void Compare(List<string> failures, string where, byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            failures.Add($"{where}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
        }
    }

    private static async Task Retire(Dictionary<string, Connection> connections, string participant)
    {
        if (connections.Remove(participant, out var connection))
        {
            await connection.DisposeAsync();
        }
    }
}
