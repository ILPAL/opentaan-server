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
    }
}
