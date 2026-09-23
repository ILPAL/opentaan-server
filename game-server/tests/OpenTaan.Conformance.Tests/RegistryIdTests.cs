namespace OpenTaan.Tests.Conformance;

/// <summary>
/// The registry id is how a packet names itself on the wire, and it is derived from
/// the class name rather than assigned. That makes renaming a class a protocol
/// change, so the derivation is checked rather than trusted.
/// </summary>
public sealed class RegistryIdTests
{
    [Fact]
    public void EveryRegistryIdIsTheChecksumOfItsName()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var wrong = WireContract.Registry.Ids
            .Where(entry => Derive(entry.Key) != entry.Value)
            .Select(entry => $"{entry.Key}: recorded {entry.Value}, derives to {Derive(entry.Key)}")
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void TheIdTableAndTheLayoutsAgree()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        var declared = WireContract.Spec.Classes.ToDictionary(c => c.WireName, c => c.RegistryId);

        var disagreeing = WireContract.Registry.Ids
            .Where(entry => declared.TryGetValue(entry.Key, out var id) && id != entry.Value)
            .Select(entry => entry.Key)
            .ToArray();

        Assert.Empty(disagreeing);
        Assert.Empty(WireContract.Registry.Ids.Keys.Except(declared.Keys));
    }

    [Fact]
    public void TheOnlyClassWithoutAnIdIsTheRootOne()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        // Every class derives from a root type that is never sent on its own, so it
        // is described by the layouts but absent from the table of things on the wire.
        var unlisted = WireContract.Spec.Classes
            .Select(c => c.WireName)
            .Except(WireContract.Registry.Ids.Keys)
            .ToArray();

        Assert.Equal(["Object"], unlisted);
    }

    [Fact]
    public void TheTableCountsItselfCorrectly()
    {
        Assert.SkipUnless(WireContract.Available, WireContract.Absence);

        Assert.Equal(WireContract.Registry.Count, WireContract.Registry.Ids.Count);
    }

    /// <summary>
    /// Adler-32 of the ASCII name, started at zero rather than the usual one.
    /// </summary>
    private static uint Derive(string name)
    {
        const uint Modulus = 65521;

        uint low = 0;
        uint high = 0;

        foreach (var c in name)
        {
            low = (low + c) % Modulus;
            high = (high + low) % Modulus;
        }

        return (high << 16) | low;
    }
}
