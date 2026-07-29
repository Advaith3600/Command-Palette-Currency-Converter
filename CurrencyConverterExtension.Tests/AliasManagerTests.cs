using CurrencyConverterExtension.Helpers;

namespace CurrencyConverterExtension.Tests;

public class AliasManagerTests
{
    [Theory]
    [InlineData("usd")]
    [InlineData("euro")]
    [InlineData("$")]
    [InlineData("\u20AC")]
    [InlineData("\u20BD")]
    [InlineData("my_alias")]
    public void ValidateKeyFormat_AcceptsValidKeys(string key)
    {
        Assert.True(AliasManager.ValidateKeyFormat(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ValidateKeyFormat_RejectsWhitespaceOrEmpty(string key)
    {
        Assert.False(AliasManager.ValidateKeyFormat(key));
    }

    [Theory]
    [InlineData("usd123")]
    [InlineData("!!!")]
    [InlineData("123")]
    [InlineData("ab cd")]
    public void ValidateKeyFormat_RejectsKeysWithInvalidCharacters(string key)
    {
        Assert.False(AliasManager.ValidateKeyFormat(key));
    }

    [Fact]
    public void HasAlias_And_GetAlias_WorkWithSeededDictionary()
    {
        var manager = new AliasManager(new Dictionary<string, string>
        {
            ["$"] = "usd",
            ["Euro"] = "eur",
        });

        Assert.True(manager.HasAlias("$"));
        Assert.Equal("usd", manager.GetAlias("$"));
        Assert.True(manager.HasAlias("euro"));
        Assert.Equal("eur", manager.GetAlias("euro"));
        Assert.False(manager.HasAlias("missing"));
        Assert.Null(manager.GetAlias("missing"));
    }

    [Fact]
    public void GetAllAliases_ReturnsSnapshotCopy()
    {
        var aliases = new Dictionary<string, string> { ["$"] = "usd" };
        var manager = new AliasManager(aliases);

        Dictionary<string, string> snapshot = manager.GetAllAliases();
        Assert.NotSame(aliases, snapshot);
        Assert.Equal("usd", snapshot["$"]);
    }
}