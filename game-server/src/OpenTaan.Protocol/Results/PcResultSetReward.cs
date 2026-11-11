using System.Buffers;

namespace OpenTaan.Protocol.Results;

/// <summary>What the result screen's cards hold: sent to everyone still in a match, just before its end.</summary>
/// <remarks>
/// <para>
/// The client keeps the rewards until <see cref="PcGameSessionEndGame"/> builds the result
/// screen, which copies them, so they always come first. It reads exactly
/// <see cref="RewardCount"/> of them, without checking how many there are or whether any is
/// missing, so every list carries that many: one reward for each character in the match,
/// and the rest for no character.
/// </para>
/// </remarks>
/// <param name="Rewards">The rewards, always <see cref="RewardCount"/> of them.</param>
[WireClass("PcResult_SetReward")]
public sealed record PcResultSetReward(IReadOnlyList<GameReward> Rewards) : IPacket
{
    /// <summary>How many rewards a list carries: one for each of a room's slots.</summary>
    public const int RewardCount = 8;

    /// <summary>The id that leads the body, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<PcResultSetReward>.RegistryId;

    /// <summary>The rewards, always <see cref="RewardCount"/> of them.</summary>
    public IReadOnlyList<GameReward> Rewards { get; } = (Rewards ?? throw new ArgumentNullException(nameof(Rewards))).Count == RewardCount
                                                        && Rewards.All(reward => reward is not null)
        ? Rewards
        : throw new ArgumentException($"A list carries exactly {RewardCount} rewards.", nameof(Rewards));

    /// <summary>Reads a body the server sent. The server never receives this packet; tests and replays do.</summary>
    public static PacketReadResult<PcResultSetReward> TryRead(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        reader.ExpectRegistryId(RegistryId);
        var rewards = reader.ReadList(GameReward.MinimumSize, GameReward.ReadFrom);
        if (reader.Error == PacketError.None && rewards.Length != RewardCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        return reader.Error == PacketError.None
            ? reader.Finish(new PcResultSetReward(rewards))
            : new PacketReadResult<PcResultSetReward>(null, reader.Error);
    }

    /// <summary>Writes the body.</summary>
    public void Write(IBufferWriter<byte> destination)
    {
        var writer = new PacketWriter(destination);
        writer.WriteUInt32(RegistryId);
        writer.WriteList(Rewards, static (list, reward) => reward.WriteTo(list));
    }

    /// <summary>Whether both lists hold the same rewards in the same order.</summary>
    public bool Equals(PcResultSetReward? other) => other is not null && Rewards.SequenceEqual(other.Rewards);

    /// <summary>A hash of every reward, consistent with <see cref="Equals(PcResultSetReward)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var reward in Rewards)
        {
            hash.Add(reward);
        }

        return hash.ToHashCode();
    }
}
