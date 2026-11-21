using OpenTaan.GameServer.Characters;

namespace OpenTaan.Tests.GameServer;

/// <summary>The client's rules for a new character's name.</summary>
public sealed class CharacterNamesTests
{
    [Theory]
    [InlineData("Kimi")]
    [InlineData("Kim0123456789ABC")]
    [InlineData("Rồng")]
    [InlineData("ĐứcAnh")]
    [InlineData("Ăn_Ưa")]
    [InlineData("[Kim]")]
    public void ANameTheClientWouldSendIsValid(string name)
    {
        Assert.True(CharacterNames.IsValid(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kim")]
    [InlineData("Kim0123456789ABCD")]
    public void ANameShorterThanFourOrLongerThanSixteenIsNot(string name)
    {
        Assert.False(CharacterNames.IsValid(name));
    }

    [Theory]
    [InlineData("Kim Anh")]
    [InlineData("Kim-Anh")]
    [InlineData("Kim\tAnh")]
    [InlineData("Kim\u0000Anh")]
    [InlineData("Roˆng")]
    [InlineData("Rông")]
    [InlineData("Kim😀A")]
    [InlineData("Kimí中")]
    public void ANameWithACharacterTheClientRefusesIsNot(string name)
    {
        Assert.False(CharacterNames.IsValid(name));
    }

    [Fact]
    public void NoNameIsNotAName()
    {
        Assert.False(CharacterNames.IsValid(null));
    }

    [Theory]
    [InlineData("Rồng", "RỒNG")]
    [InlineData("kimi", "KIMI")]
    [InlineData("Đức_A", "đỨC_a")]
    public void NamesThatDifferOnlyInCaseAreOneName(string first, string second)
    {
        Assert.True(CharacterNames.Comparer.Equals(first, second));
        Assert.Equal(CharacterNames.KeyOf(first), CharacterNames.KeyOf(second));
    }

    [Fact]
    public void TwoNamesHaveOneKeyExactlyWhenTheyAreOneName()
    {
        // Every character a name may hold, each against every other. Names are compared a
        // character at a time, so agreeing on every pair is agreeing on every name.
        var allowed = Enumerable.Range(0, char.MaxValue + 1)
            .Select(code => (char)code)
            .Where(character => CharacterNames.IsValid(new string(character, CharacterNames.MinimumLength)))
            .Select(character => character.ToString())
            .ToArray();
        Assert.Contains("ỹ", allowed);

        var disagreements = (
            from first in allowed
            from second in allowed
            where CharacterNames.Comparer.Equals(first, second) != (CharacterNames.KeyOf(first) == CharacterNames.KeyOf(second))
            select $"{first} {second}").Take(10).ToArray();

        Assert.Empty(disagreements);
    }

    [Fact]
    public void ANameKeysTheSameHoweverItsLettersAreCased()
    {
        Assert.Equal("RỒNG VÀNG", CharacterNames.KeyOf("rồng vàng"));
        Assert.Equal(CharacterNames.KeyOf("Đức_A"), CharacterNames.KeyOf("đỨC_a"));
    }

    [Fact]
    public void ANameWithTheLongSIsTheNameWithS()
    {
        // The one letter .NET's own case-insensitive comparison takes apart from its capital.
        Assert.True(CharacterNames.Comparer.Equals("Kiſi", "Kisi"));
        Assert.True(CharacterNames.Comparer.Equals("Kiſi", "KISI"));
        Assert.Equal(CharacterNames.Comparer.GetHashCode("Kiſi"), CharacterNames.Comparer.GetHashCode("kisi"));
    }

    [Fact]
    public void NamesThatDifferInMoreThanCaseAreTwoNames()
    {
        Assert.False(CharacterNames.Comparer.Equals("Rồng", "Rông"));
        Assert.False(CharacterNames.Comparer.Equals("Kimi", "Kimo"));
        Assert.NotEqual(0, CharacterNames.Comparer.Compare("Kimi", "Kimo"));
    }
}
