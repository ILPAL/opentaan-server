using Microsoft.Extensions.Options;
using OpenTaan.GameServer.Sessions;
using OpenTaan.Protocol.PlayTime;

namespace OpenTaan.GameServer.PlayTime;

/// <summary>Tells a client how long its player may play, as it enters a channel.</summary>
/// <remarks>
/// <para>
/// A client of a build with a play-time limit checks the limit as it builds its lobby,
/// before it asks to enter it, so the grant goes out with the answer that sends the client
/// there, ahead of it. A client of a build without the limit ignores all three packets.
/// </para>
/// <para>
/// The order is the client's: the limit for each account slot, then the played time and
/// counting for each selected character, then a reset. The middle packet also sets the
/// played time, so only the reset after it leaves every slot at 0.
/// </para>
/// </remarks>
public sealed class PlayTimeGrant(IOptions<PlayTimeOptions> options, TimeProvider time)
{
    // What the client reads as no limit: no played time reaches it.
    private const uint Unlimited = uint.MaxValue;

    /// <summary>Sends the grant for the connection's accounts and selected characters.</summary>
    public void SendTo(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var allowed = options.Value.AllowedSeconds ?? Unlimited;
        var now = (uint)time.GetUtcNow().ToUnixTimeSeconds();

        for (var slot = 0; slot < session.Accounts.Count; slot++)
        {
            session.Send(new PcNotifyPlayableTime(
                SlotIndex: (byte)slot,
                Timestamp: now,
                PlayedSeconds: 0,
                AllowedSeconds: allowed,
                MaxPlaySeconds: allowed,
                ProgressRatio: 0));
        }

        foreach (var characterId in session.SelectedCharacterIds)
        {
            session.Send(new PcPlayableTimeCtrl(characterId, PlayedSeconds: 0, Counting: true));
        }

        session.Send(new PcResetPlayableTime());
    }
}
