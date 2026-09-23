using System.Reflection;
using OpenTaan.Protocol;
using OpenTaan.Protocol.Accounts;
using OpenTaan.Protocol.Handshake;

namespace OpenTaan.Tests.Protocol;

/// <summary>Registry ids computed from wire names, and the attribute that carries the names.</summary>
public sealed class WireIdentityTests
{
    [Fact]
    public void StartsTheRunningSumAtZeroNotOne()
    {
        // Standard Adler-32 of "A" is 0x00420042. Starting at zero gives one less in
        // each half, and a standard library would get every id wrong this way.
        Assert.Equal(0x00410041u, RegistryIds.Of("A"));
    }

    [Theory]
    [InlineData("PcFirstAccept", 0x221A050Bu)]
    [InlineData("PrUserLogin", 0x1995045Au)]
    [InlineData("AccountInfo", 0x19500459u)]
    public void ComputesTheIdsTheClientExpects(string wireName, uint expected)
    {
        Assert.Equal(expected, RegistryIds.Of(wireName));
    }

    [Fact]
    public void RefusesANameTheClientCouldNotHave()
    {
        Assert.Throws<ArgumentException>(() => RegistryIds.Of("Pé"));
        Assert.Throws<ArgumentException>(() => RegistryIds.Of(string.Empty));
    }

    [Fact]
    public void ReadsTheIdFromTheAttribute()
    {
        Assert.Equal("PcFirstAccept", WireIdentity<PcFirstAccept>.Name);
        Assert.Equal(PcFirstAccept.RegistryId, RegistryIds.Of("PcFirstAccept"));
    }

    [Fact]
    public void BothLoginAnswersCarryTheLoginAnswersId()
    {
        // The accepted and refused layouts are one packet on the wire.
        Assert.Equal(PrUserLogin.RegistryId, WireIdentity<PrUserLoginSucceeded>.RegistryId);
        Assert.Equal(PrUserLogin.RegistryId, WireIdentity<PrUserLoginFailed>.RegistryId);
    }

    [Fact]
    public void EveryTypeWithARegistryIdIsAWireClass()
    {
        // A type that forgot the attribute would fail on first use, at run time.
        // WireIdentity<T> is the lookup itself, not a wire class.
        var unmarked = ProtocolTypes()
            .Where(type => !type.IsGenericTypeDefinition)
            .Where(type => type.GetProperty("RegistryId", BindingFlags.Public | BindingFlags.Static) is not null)
            .Where(type => type.GetCustomAttribute<WireClassAttribute>() is null)
            .Select(type => type.Name)
            .ToArray();

        Assert.Empty(unmarked);
    }

    [Fact]
    public void EveryWireClassIsNamedLikeItsWireNameWithoutUnderscores()
    {
        var misnamed = ProtocolTypes()
            .Select(type => (type.Name, Attribute: type.GetCustomAttribute<WireClassAttribute>(inherit: false)))
            .Where(type => type.Attribute is not null && type.Attribute.Name.Replace("_", string.Empty, StringComparison.Ordinal) != type.Name)
            .Select(type => $"{type.Name} is registered as {type.Attribute!.Name}")
            .ToArray();

        Assert.Empty(misnamed);
    }

    private static IEnumerable<Type> ProtocolTypes() => typeof(ProtocolAssemblyMarker).Assembly.GetExportedTypes();
}
