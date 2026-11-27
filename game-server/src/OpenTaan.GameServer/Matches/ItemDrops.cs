using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTaan.GameServer.Matches;

/// <summary>
/// One row of the client's item drop table: an item, the drop qualities it drops at, and how
/// likely it is at each end of them, as the client weighs its own draws.
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="MinQuality">The lowest drop quality the item drops at.</param>
/// <param name="MaxQuality">The highest drop quality the item drops at.</param>
/// <param name="WeightAtMin">How likely the item is at <paramref name="MinQuality"/>, against the other rows.</param>
/// <param name="WeightAtMax">How likely the item is at <paramref name="MaxQuality"/>, against the other rows.</param>
public sealed record ItemDropRow(ushort ItemId, int MinQuality, int MaxQuality, int WeightAtMin, int WeightAtMax);

/// <summary>The items a box or a bonus gives in a match between teams, and the server's draw of one.</summary>
/// <remarks>
/// <para>
/// They are the items of the client's own drop table, which is the client's data and so is
/// never part of this repository: whoever runs the server keeps the table's rows in a JSON
/// file, which <see cref="MatchOptions.ItemDropsFile"/> names, and the server refuses to
/// start without it.
/// </para>
/// <para>
/// The client draws from the rows by their weights, at the drop quality the start of the
/// turn tells it. The server draws every item the table has as likely as any other,
/// whatever the quality, so the weights only make the file a faithful copy of the table.
/// </para>
/// </remarks>
public sealed class ItemDrops
{
    /// <summary>The item the client gives for "no item", which no row may name.</summary>
    public const ushort NoItem = 0;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The items of <paramref name="rows"/>, each once, in the order the rows have them.</summary>
    /// <exception cref="ArgumentException">There are no rows, or a row names no item.</exception>
    public ItemDrops(IReadOnlyList<ItemDropRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new ArgumentException("A drop table has at least one row.", nameof(rows));
        }

        if (rows.Any(row => row.ItemId == NoItem))
        {
            throw new ArgumentException($"A drop table's rows name an item, never {NoItem}, which the client gives for no item.", nameof(rows));
        }

        Items = [.. rows.Select(row => row.ItemId).Distinct()];
    }

    /// <summary>The items a box or a bonus can give, each once.</summary>
    public IReadOnlyList<ushort> Items { get; }

    /// <summary>Whether a box or a bonus can give <paramref name="itemId"/>.</summary>
    public bool Gives(ushort itemId) => Items.Contains(itemId);

    /// <summary>Draws the item a box or a bonus gives: any of <see cref="Items"/>, each as likely as another.</summary>
    public ushort Draw(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return Items[random.Next(Items.Count)];
    }

    /// <summary>
    /// Reads the drop table from <paramref name="file"/>, found as <see cref="LocalFiles.Locate"/>
    /// finds a file from <paramref name="contentRoot"/>.
    /// </summary>
    /// <param name="file">The file the setting names, or null when it names none.</param>
    /// <param name="contentRoot">Where a relative <paramref name="file"/> is looked for first.</param>
    /// <param name="found">Where the file was found, once it is.</param>
    /// <param name="problem">Why there is no table, for the message the server refuses to start with.</param>
    /// <returns>The table, or null when it cannot be had.</returns>
    public static ItemDrops? Load(string? file, string contentRoot, out string? found, out string? problem)
    {
        const string Setting = $"{MatchOptions.Section}:{nameof(MatchOptions.ItemDropsFile)}";
        found = null;
        if (string.IsNullOrEmpty(file))
        {
            problem = $"{Setting} must name the JSON file of the client's item drop table, which boxes and bonuses give their items from";
            return null;
        }

        found = LocalFiles.Locate(file, contentRoot);
        if (found is null)
        {
            problem = $"{Setting} names {file}, which is not in {contentRoot} or any folder above it";
            return null;
        }

        try
        {
            var table = JsonSerializer.Deserialize<ItemDropTable>(File.ReadAllBytes(found), Json)
                ?? throw new JsonException("The file holds null.");
            problem = null;
            return new ItemDrops(table.Drops);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            problem = $"The item drop table in {found} cannot be used: {exception.Message}";
            return null;
        }
    }

    // The file: the table's rows under "drops".
    private sealed record ItemDropTable(IReadOnlyList<ItemDropRow> Drops);
}
