using OpenTaan.GameServer.Matches;

namespace OpenTaan.Tests.GameServer;

/// <summary>
/// The drop table the server draws the items of boxes and bonuses from: its items, the even
/// draw among them, and reading it from its file, which the server cannot start without.
/// Every item and file is invented.
/// </summary>
public sealed class ItemDropsTests : IDisposable
{
    private const string Setting = "Matches:ItemDropsFile";

    private const string ValidFile = """
        {
          "drops": [
            { "itemId": 2561, "minQuality": 0, "maxQuality": 100, "weightAtMin": 10, "weightAtMax": 20 },
            { "itemId": 2562, "minQuality": 40, "maxQuality": 100, "weightAtMin": 1, "weightAtMax": 5 }
          ]
        }
        """;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("item-drops-");

    [Fact]
    public void TheItemsAreTheRowsItemsEachOnceInTheOrderTheRowsHaveThem()
    {
        var drops = new ItemDrops([Row(0x0A02), Row(0x0A01), Row(0x0A02), Row(0x0A03)]);

        Assert.Equal([0x0A02, 0x0A01, 0x0A03], drops.Items);
        Assert.True(drops.Gives(0x0A03));
        Assert.False(drops.Gives(0x0A04));
    }

    [Fact]
    public void ADrawTakesAnyItemAsLikelyAsAnotherWhateverTheRowsWeigh()
    {
        var drops = new ItemDrops([Row(0x0A01, weight: 1), Row(0x0A02, weight: 1000), Row(0x0A03, weight: 1)]);
        var chance = new ScriptedChance(seed: 0);
        foreach (var draw in new[] { 2, 0, 1 })
        {
            chance.Draws.Enqueue(draw);
        }

        Assert.Equal([0x0A03, 0x0A01, 0x0A02], new[] { drops.Draw(chance), drops.Draw(chance), drops.Draw(chance) });
        Assert.Equal([3, 3, 3], chance.DrawnAmong);
    }

    [Fact]
    public void ATableWithNoRowsOrWithARowNamingNoItemIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ItemDrops([]));
        Assert.Throws<ArgumentException>(() => new ItemDrops([Row(0x0A01), Row(ItemDrops.NoItem)]));
    }

    [Fact]
    public void ATableIsReadFromItsFileFoundInAFolderAboveTheContentRoot()
    {
        // As when the server is started from its build output rather than its project.
        var contentRoot = _root.CreateSubdirectory(Path.Combine("src", "bin", "Debug"));
        var data = _root.CreateSubdirectory("client-data");
        File.WriteAllText(Path.Combine(data.FullName, "item-drops.json"), ValidFile);

        var drops = ItemDrops.Load(Path.Combine("client-data", "item-drops.json"), contentRoot.FullName, out var found, out var problem);

        Assert.Null(problem);
        Assert.Equal(Path.Combine(data.FullName, "item-drops.json"), found);
        Assert.Equal([2561, 2562], drops!.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoFileNamedIsRefusedNamingTheSetting(string? file)
    {
        Assert.Null(ItemDrops.Load(file, _root.FullName, out _, out var problem));
        Assert.Contains(Setting, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileNotThereIsRefusedNamingItAndWhereItWasLookedFor()
    {
        Assert.Null(ItemDrops.Load("missing.json", _root.FullName, out _, out var problem));
        Assert.Contains("missing.json", problem, StringComparison.Ordinal);
        Assert.Contains(_root.FullName, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "drops": [] }""")]
    [InlineData("""{ "drops": [ { "itemId": 0, "minQuality": 0, "maxQuality": 100, "weightAtMin": 1, "weightAtMax": 1 } ] }""")]
    [InlineData("""{ "drops": [ { "itemId": 2561, "minQuality": 0, "maxQuality": 100, "weightAtMin": 1 } ] }""")]
    [InlineData("""{ "drops": [ { "itemId": 2561, "minQuality": 0, "maxQuality": 100, "weightAtMin": 1, "weightAtMax": 1, "wieght": 1 } ] }""")]
    [InlineData("""{ "rows": [ { "itemId": 2561, "minQuality": 0, "maxQuality": 100, "weightAtMin": 1, "weightAtMax": 1 } ] }""")]
    public void AFileThatIsNoTableOfItemsIsRefusedNamingIt(string contents)
    {
        var path = Path.Combine(_root.FullName, "item-drops.json");
        File.WriteAllText(path, contents);

        Assert.Null(ItemDrops.Load("item-drops.json", _root.FullName, out _, out var problem));
        Assert.Contains(path, problem, StringComparison.Ordinal);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private static ItemDropRow Row(ushort item, int weight = 1) => new(item, MinQuality: 0, MaxQuality: 100, weight, weight);
}
