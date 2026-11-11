namespace OpenTaan.Protocol.GamePlay;

/// <summary>
/// A change to a team's gauge that a packet may carry: a one-byte flag, then, when it is
/// set, the change itself, a step of the gauge kind with its own id.
/// </summary>
/// <remarks>
/// The end of a turn, a box caught and a bonus item all carry one this way, so it is read
/// and written here once for all three.
/// </remarks>
internal static class TeamGaugeChange
{
    /// <summary>Reads the flag, then the change it flags; refuses a flag other than 0 or 1, or a step of another kind.</summary>
    public static GameScenarioCommandTeamGauge? ReadOptional(ref PacketReader reader)
    {
        if (!reader.ReadBoolean() || reader.Error != PacketError.None)
        {
            return null;
        }

        var change = GameScenarioCommand.ReadFrom(ref reader) as GameScenarioCommandTeamGauge;
        if (change is null)
        {
            // A step of another kind where only a gauge change belongs.
            reader.Fail(PacketError.UnexpectedValue);
        }

        return change;
    }

    /// <summary>Writes the flag, then <paramref name="change"/> when there is one.</summary>
    public static void WriteOptional(PacketWriter writer, GameScenarioCommandTeamGauge? change)
    {
        writer.WriteBoolean(change is not null);
        change?.WriteTo(writer);
    }
}

