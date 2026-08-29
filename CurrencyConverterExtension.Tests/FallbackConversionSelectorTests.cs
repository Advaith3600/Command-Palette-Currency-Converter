using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;

namespace CurrencyConverterExtension.Tests;

public class FallbackConversionSelectorTests
{
    private static AliasManager Aliases() => new(new Dictionary<string, string>
    {
        ["$"] = "usd",
        ["euro"] = "eur",
    });

    private static FallbackConversionPair? Select(
        string from,
        string to,
        string local = "usd",
        string[]? currencies = null,
        AliasManager? aliases = null) =>
        FallbackConversionSelector.TrySelect(
            new ParsedQuery(300m, from, to),
            local,
            currencies ?? ["eur", "inr"],
            aliases ?? Aliases());

    [Fact]
    public void AmountOnly_ConvertsLocalToFirstGlobal()
    {
        FallbackConversionPair? pair = Select(from: "", to: "");

        Assert.NotNull(pair);
        Assert.Equal("usd", pair.Value.FromCurrency);
        Assert.Equal("eur", pair.Value.ToCurrency);
    }

    [Fact]
    public void FromOnly_ConvertsToLocal()
    {
        FallbackConversionPair? pair = Select(from: "cny", to: "");

        Assert.NotNull(pair);
        Assert.Equal("cny", pair.Value.FromCurrency);
        Assert.Equal("usd", pair.Value.ToCurrency);
    }

    [Fact]
    public void FromOnly_WhenFromIsLocal_ConvertsToFirstGlobal()
    {
        FallbackConversionPair? pair = Select(from: "usd", to: "");

        Assert.NotNull(pair);
        Assert.Equal("usd", pair.Value.FromCurrency);
        Assert.Equal("eur", pair.Value.ToCurrency);
    }

    [Fact]
    public void ExplicitPair_UsesFromAndTo()
    {
        FallbackConversionPair? pair = Select(from: "cny", to: "eur");

        Assert.NotNull(pair);
        Assert.Equal("cny", pair.Value.FromCurrency);
        Assert.Equal("eur", pair.Value.ToCurrency);
    }

    [Fact]
    public void ToOnly_ConvertsLocalToTarget()
    {
        FallbackConversionPair? pair = Select(from: "", to: "eur");

        Assert.NotNull(pair);
        Assert.Equal("usd", pair.Value.FromCurrency);
        Assert.Equal("eur", pair.Value.ToCurrency);
    }

    [Fact]
    public void AliasFromMatchingLocal_ConvertsToFirstGlobal()
    {
        FallbackConversionPair? pair = Select(from: "$", to: "");

        Assert.NotNull(pair);
        Assert.Equal("usd", pair.Value.FromCurrency);
        Assert.Equal("eur", pair.Value.ToCurrency);
    }

    [Fact]
    public void LocalCurrencyAlias_ComparedCaseInsensitively()
    {
        FallbackConversionPair? pair = Select(from: "cny", to: "", local: "USD");

        Assert.NotNull(pair);
        Assert.Equal("cny", pair.Value.FromCurrency);
        Assert.Equal("usd", pair.Value.ToCurrency);
    }

    [Fact]
    public void AmountOnly_WhenFirstGlobalEqualsLocal_ReturnsNull()
    {
        FallbackConversionPair? pair = Select(from: "", to: "", local: "usd", currencies: ["usd", "eur"]);

        Assert.Null(pair);
    }

    [Fact]
    public void FromIsLocal_WhenFirstGlobalEqualsLocal_ReturnsNull()
    {
        FallbackConversionPair? pair = Select(from: "usd", to: "", local: "usd", currencies: ["USD"]);

        Assert.Null(pair);
    }

    [Fact]
    public void ExplicitSameCurrency_ReturnsNull()
    {
        FallbackConversionPair? pair = Select(from: "usd", to: "usd");

        Assert.Null(pair);
    }

    [Fact]
    public void AmountOnly_WhenCurrenciesEmpty_ReturnsNull()
    {
        FallbackConversionPair? pair = Select(from: "", to: "", currencies: []);

        Assert.Null(pair);
    }
}
