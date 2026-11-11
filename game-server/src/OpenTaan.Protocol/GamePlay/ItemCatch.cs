using OpenTaan.Protocol.Rooms;

namespace OpenTaan.Protocol.GamePlay;

/// <summary>
/// What a box caught and a bonus item both carry: the slot of the character that gets the
/// item, the catch itself as a step of the catching kind with its own id, then a change to
/// a team's gauge it may carry. Read and written here once for both.
/// </summary>
internal static class ItemCatch
{
    /// <summary>A catch put in place of one that could not be read; the read has failed by then.</summary>
    private static readonly GameScenarioCommandEatItemBox Unread = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Reads the three; refuses a slot outside the room, or a step of another kind where the catch belongs.</summary>
    public static (byte SlotIndex, GameScenarioCommandEatItemBox Catch, GameScenarioCommandTeamGauge? TeamGauge) Read(ref PacketReader reader)
    {
        var slotIndex = reader.ReadByte();
        var caught = GameScenarioCommand.ReadFrom(ref reader) as GameScenarioCommandEatItemBox;
        if (caught is null)
        {
            reader.Fail(PacketError.UnexpectedValue);
            caught = Unread;
        }

        var teamGauge = TeamGaugeChange.ReadOptional(ref reader);
        if (slotIndex >= GameSlotInfo.SlotCount)
        {
            reader.Fail(PacketError.UnexpectedValue);
            slotIndex = 0;
        }

        return (slotIndex, caught, teamGauge);
    }

    /// <summary>Writes the three, as the client wrote them.</summary>
    public static void Write(PacketWriter writer, byte slotIndex, GameScenarioCommandEatItemBox caught, GameScenarioCommandTeamGauge? teamGauge)
    {
        writer.WriteByte(slotIndex);
        caught.WriteTo(writer);
        TeamGaugeChange.WriteOptional(writer, teamGauge);
    }

    /// <summary>Checks a slot given to a packet that carries a catch.</summary>
    public static byte Slot(byte slotIndex) => slotIndex < GameSlotInfo.SlotCount
        ? slotIndex
        : throw new ArgumentOutOfRangeException(nameof(slotIndex), $"A room has slots 0 to {GameSlotInfo.SlotCount - 1}.");
}
