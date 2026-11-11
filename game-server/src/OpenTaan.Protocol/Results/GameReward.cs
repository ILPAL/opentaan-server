namespace OpenTaan.Protocol.Results;

/// <summary>What one character's result card holds: a reward of one kind, named for one character.</summary>
/// <remarks>
/// <para>
/// The client finds a character's reward by the character, not by the card: when a player
/// turns a card over, the client shows the reward naming that player's character, whatever
/// card was picked. Turning a card over only shows the reward; whatever it gives is the
/// server's to give.
/// </para>
/// <para>
/// Each kind is a class of its own on the wire, and also carries its kind as a number,
/// which <see cref="RewardType"/> derives from the class. Of the four kinds the client
/// knows, the codecs read the two the reference sessions carry, experience and gold; a list
/// holding another kind is refused as one this server does not read yet.
/// </para>
/// </remarks>
/// <param name="Name">The reward's name, which the client shows for some kinds.</param>
/// <param name="CharacterId">The character the reward is for, or 0 for a card no character's.</param>
public abstract record GameReward(string Name, uint CharacterId)
{
    /// <summary>The fewest bytes a reward occupies: its id, an empty name, its kind and its character.</summary>
    internal const int MinimumSize = sizeof(uint) + sizeof(uint) + sizeof(byte) + sizeof(uint);

    /// <summary>The reward's name, which the client shows for some kinds.</summary>
    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <summary>The kind of reward, as the wire numbers it.</summary>
    public abstract byte RewardType { get; }

    /// <summary>The id that leads this kind, computed from its wire name.</summary>
    private protected abstract uint KindRegistryId { get; }

    /// <summary>Reads a reward of either kind, or refuses the read.</summary>
    internal static GameReward ReadFrom(ref PacketReader reader)
    {
        var id = reader.ReadUInt32();
        if (reader.Error != PacketError.None)
        {
            return GameRewardGold.Unread;
        }

        if (id != GameRewardExp.RegistryId && id != GameRewardGold.RegistryId)
        {
            // The quest and item kinds are rewards the client knows, which nothing here
            // reads yet; any other class is not a reward at all.
            reader.Fail(id == RegistryIds.Of("GameRewardQuest") || id == RegistryIds.Of("GameRewardItem")
                ? PacketError.Unsupported
                : PacketError.WrongRegistryId);
            return GameRewardGold.Unread;
        }

        var name = reader.ReadUtf16String();
        var rewardType = reader.ReadByte();
        var characterId = reader.ReadUInt32();
        GameReward reward = id == GameRewardExp.RegistryId
            ? GameRewardExp.ReadRest(ref reader, name, characterId)
            : GameRewardGold.ReadRest(ref reader, name, characterId);

        if (rewardType != reward.RewardType)
        {
            reader.Fail(PacketError.UnexpectedValue);
        }

        return reward;
    }

    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteUInt32(KindRegistryId);
        writer.WriteUtf16String(Name);
        writer.WriteByte(RewardType);
        writer.WriteUInt32(CharacterId);
        WriteRest(writer);
    }

    /// <summary>Writes the fields this kind adds after the ones every reward has.</summary>
    private protected abstract void WriteRest(PacketWriter writer);
}

/// <summary>Experience, on a result card.</summary>
/// <param name="Name">The reward's name.</param>
/// <param name="CharacterId">The character the reward is for.</param>
/// <param name="Amount">The experience shown.</param>
[WireClass("GameRewardExp")]
public sealed record GameRewardExp(string Name, uint CharacterId, uint Amount) : GameReward(Name, CharacterId)
{
    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameRewardExp>.RegistryId;

    /// <summary>The kind of reward, as the wire numbers it: experience.</summary>
    public override byte RewardType => 3;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameRewardExp ReadRest(ref PacketReader reader, string name, uint characterId) =>
        new(name, characterId, reader.ReadUInt32());

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer) => writer.WriteUInt32(Amount);
}

/// <summary>Gold, on a result card.</summary>
/// <param name="Name">The reward's name, which the client does not show for gold.</param>
/// <param name="CharacterId">The character the reward is for, or 0 for a card no character's.</param>
/// <param name="Amount">The gold shown.</param>
/// <param name="HasPcRoomBonusCard">Whether the player also turns over a second card, the bonus card of a game room.</param>
/// <param name="PcRoomBonusGold">The gold that bonus card shows.</param>
[WireClass("GameRewardGold")]
public sealed record GameRewardGold(string Name, uint CharacterId, uint Amount, bool HasPcRoomBonusCard, uint PcRoomBonusGold)
    : GameReward(Name, CharacterId)
{
    /// <summary>A reward put in place of one that could not be read; the read has failed by then.</summary>
    internal static readonly GameRewardGold Unread = new(string.Empty, 0, 0, false, 0);

    /// <summary>The id that leads it, computed from the wire name.</summary>
    public static uint RegistryId => WireIdentity<GameRewardGold>.RegistryId;

    /// <summary>The kind of reward, as the wire numbers it: gold.</summary>
    public override byte RewardType => 4;

    /// <inheritdoc/>
    private protected override uint KindRegistryId => RegistryId;

    internal static GameRewardGold ReadRest(ref PacketReader reader, string name, uint characterId)
    {
        var amount = reader.ReadUInt32();
        var hasPcRoomBonusCard = reader.ReadBoolean();
        var pcRoomBonusGold = reader.ReadUInt32();
        return new GameRewardGold(name, characterId, amount, hasPcRoomBonusCard, pcRoomBonusGold);
    }

    /// <inheritdoc/>
    private protected override void WriteRest(PacketWriter writer)
    {
        writer.WriteUInt32(Amount);
        writer.WriteBoolean(HasPcRoomBonusCard);
        writer.WriteUInt32(PcRoomBonusGold);
    }
}
