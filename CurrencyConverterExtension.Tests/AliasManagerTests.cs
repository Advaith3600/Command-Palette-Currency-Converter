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
        var manager = new AliasManager();
        Assert.True(manager.ValidateKeyFormat(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ValidateKeyFormat_RejectsWhitespaceOrEmpty(string key)
    {
        var manager = new AliasManager();
        Assert.False(manager.ValidateKeyFormat(key));
    }

    [Fact]
    public void ValidateKeyFormat_CurrentRegexAllowsDigitsAndMixedContent()
    {
        // KeyRegex uses * with Match (not full-string ^...$), so this documents current behavior.
        var manager = new AliasManager();
        Assert.True(manager.ValidateKeyFormat("usd123"));
        Assert.True(manager.ValidateKeyFormat("!!!"));
    }

    [Fact]
    public void HasAlias_And_GetAlias_WorkWithSeededDictionary()
    {
        var manager = new AliasManager(new Dictionary<string, string>
        {
            ["$"] = "usd",
            ["euro"] = "eur",
        });

        Assert.True(manager.HasAlias("$"));
        Assert.Equal("usd", manager.GetAlias("$"));
        Assert.True(manager.HasAlias("euro"));
        Assert.Equal("eur", manager.GetAlias("euro"));
        Assert.False(manager.HasAlias("missing"));
        Assert.Null(manager.GetAlias("missing"));
    }

    [Fact]
    public void GetAllAliases_ReturnsSeededDictionary()
    {
        var aliases = new Dictionary<string, string> { ["$"] = "usd" };
        var manager = new AliasManager(aliases);

        Assert.Same(aliases, manager.GetAllAliases());
        Assert.Equal("usd", manager.GetAllAliases()["$"]);
    }
}