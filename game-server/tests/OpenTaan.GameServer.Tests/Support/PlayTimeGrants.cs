using System.Buffers.Binary;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.Channels;
using OpenTaan.Protocol.PlayTime;

namespace OpenTaan.Tests.GameServer;

/// <summary>The play time a connection is granted as it enters a channel.</summary>
/// <param name="Limits">The limit for each account slot, in slot order.</param>
/// <param name="Counters">The played time and counting for each selected character.</param>
internal sealed record PlayTimeGrantReceived(
    IReadOnlyList<PcNotifyPlayableTime> Limits,
    IReadOnlyList<PcPlayableTimeCtrl> Counters);

/// <summary>Reads a channel move's answer, and the play-time grant that comes ahead of an entered one.</summary>
internal static class PlayTimeGrants
{
    /// <summary>
    /// Reads the answer to a channel move, with the grant before it when there is one.
    /// The grant is checked for being whole: its limits, then its counters, then a reset.
    /// </summary>
    public static async Task<(PlayTimeGrantReceived? Grant, PrChannelMoveTo Answer)> ReceiveMoveAnswerAsync(this LoopbackClient client)
    {
        var body = await client.ReceiveBodyAsync();
        if (BinaryPrimitives.ReadUInt32LittleEndian(body) != PcNotifyPlayableTime.RegistryId)
        {
            return (null, Read(PrChannelMoveTo.TryRead, body));
        }

        var limits = new List<PcNotifyPlayableTime>();
        var counters = new List<PcPlayableTimeCtrl>();
        while (BinaryPrimitives.ReadUInt32LittleEndian(body) == PcNotifyPlayableTime.RegistryId)
        {
            limits.Add(Read(PcNotifyPlayableTime.TryRead, body));
            body = await client.ReceiveBodyAsync();
        }

        while (BinaryPrimitives.ReadUInt32LittleEndian(body) == PcPlayableTimeCtrl.RegistryId)
        {
            counters.Add(Read(PcPlayableTimeCtrl.TryRead, body));
            body = await client.ReceiveBodyAsync();
        }

        Read(PcResetPlayableTime.TryRead, body);
        var answer = await client.ReceiveAsync<PrChannelMoveTo>(PrChannelMoveTo.TryRead);
        return (new PlayTimeGrantReceived(limits, counters), answer);
    }

    private static TPacket Read<TPacket>(PacketDecoder<TPacket> decode, byte[] body)
        where TPacket : class
    {
        var read = decode(body);
        Assert.True(read.Succeeded, $"expected {typeof(TPacket).Name}, which refused the next body: {read.Error}");
        return read.Packet;
    }
}
