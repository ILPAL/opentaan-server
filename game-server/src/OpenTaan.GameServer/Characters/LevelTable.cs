namespace OpenTaan.GameServer.Characters;

/// <summary>
/// The experience each level takes, from which a character's level and experience bar are
/// worked out.
/// </summary>
/// <remarks>
/// <para>
/// The client has no table of levels. It draws the experience bar from the three numbers it
/// is sent, the character's experience, the experience at which its level begins and the
/// experience at which the next begins, and it shows whatever level it is told. So the
/// levels are the server's own. The client draws a level's badge only below 110, so the
/// last level is 109.
/// </para>
/// <para>
/// Experience is a running total. Each level begins where the one below it ends, and costs
/// more than it: the costs rise steeply at first and more slowly later, and reaching the
/// last level takes 17,519,999 in all. The last level's bar ends too, as the client divides
/// by its length, and a character's experience stops at that end, its bar full.
/// </para>
/// </remarks>
public static class LevelTable
{
    /// <summary>The last level: the client draws no badge for a higher one.</summary>
    public const ushort LastLevel = 109;

    // The experience each level takes, from its beginning to the next level's, first level
    // first. The last level's is the length of its bar.
    private static readonly uint[] Costs =
    [
        300, 491, 658, 861, 1063, 2000, 2508, 3138, 3892, 4638, 5513, 6517, 7519, 8652, 9918, 11183,
        12582, 14114, 15646, 17312, 19113, 20915, 22851, 24922, 26994, 29201, 31543, 33886, 36365, 38979,
        41594, 44345, 47232, 50119, 53142, 56302, 59462, 62758, 66191, 69624, 73193, 76899, 80605, 84448,
        88427, 92408, 96524, 100777, 105031, 109421, 112434, 115529, 118710, 121978, 125336, 128786,
        132332, 135975, 139712, 143565, 147517, 151579, 155752, 160039, 164445, 168973, 173625, 178405,
        183316, 188363, 193549, 198877, 204352, 209978, 215759, 221699, 227802, 234074, 240518, 247140,
        253943, 260935, 268118, 275500, 283084, 290878, 298886, 307114, 315569, 324257, 333184, 342357,
        351782, 361467, 371418, 381643, 392150, 402946, 414040, 425438, 437151, 449186, 461552, 474259,
        487316, 500732, 514517, 528682, 543237,
    ];

    // Where each level begins, and after the last, where the last level's bar ends.
    private static readonly uint[] Starts = StartsOf(Costs);

    /// <summary>The most experience a character can have: the end of the last level's bar.</summary>
    public static uint MostExperience => Starts[^1];

    /// <summary>
    /// The experience the first level takes. No level takes less, so a match that pays no
    /// more than this moves a character up at most one level.
    /// </summary>
    public static uint FirstLevelCost => Costs[0];

    /// <summary>The experience bar at <paramref name="level"/>, or null for a level past the table.</summary>
    public static ExperienceBar? BarAt(ushort level) =>
        level is >= Character.StartingLevel and <= LastLevel ? new ExperienceBar(Starts[level - 1], Starts[level]) : null;

    /// <summary>The level a character with <paramref name="experience"/> in all has reached.</summary>
    public static ushort LevelOf(uint experience)
    {
        // The last level whose beginning the experience has reached.
        var found = Array.BinarySearch(Starts, 0, LastLevel, experience);
        var level = found >= 0 ? found + 1 : ~found;
        return (ushort)level;
    }

    private static uint[] StartsOf(uint[] costs)
    {
        if (costs.Length != LastLevel)
        {
            throw new InvalidOperationException($"The table gives a cost for each of the {LastLevel} levels.");
        }

        var starts = new uint[costs.Length + 1];
        for (var level = 0; level < costs.Length; level++)
        {
            if (level > 0 && costs[level] <= costs[level - 1])
            {
                throw new InvalidOperationException("Each level costs more than the one below it.");
            }

            starts[level + 1] = checked(starts[level] + costs[level]);
        }

        return starts;
    }
}
