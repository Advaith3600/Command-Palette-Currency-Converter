using CurrencyConverterExtension.Helpers;

namespace CurrencyConverterExtension.Tests;

public class PinnedConversionManagerTests
{
    [Fact]
    public void SeededManager_GetAllPins_ReturnsSnapshot()
    {
        var pins = new List<PinnedConversion>
        {
            new(34m, "btc", "aed"),
        };
        var manager = new PinnedConversionManager(pins);

        List<PinnedConversion> snapshot = manager.GetAllPins();
        Assert.Single(snapshot);
        Assert.Equal(34m, snapshot[0].Amount);
        Assert.Equal("btc", snapshot[0].FromCurrency);
        Assert.Equal("aed", snapshot[0].ToCurrency);
    }

    [Fact]
    public void SeededManager_NormalizesAndDedupes()
    {
        var manager = new PinnedConversionManager(
        [
            new(10m, "USD", "eur"),
            new(10m, "usd", "EUR"),
            new(5m, " BTC ", "aed"),
        ]);

        List<PinnedConversion> pins = manager.GetAllPins();
        Assert.Equal(2, pins.Count);
        Assert.Contains(pins, p => p.Equals(new PinnedConversion(10m, "usd", "eur")));
        Assert.Contains(pins, p => p.Equals(new PinnedConversion(5m, "btc", "aed")));
    }

    [Fact]
    public void Contains_IsCaseInsensitiveOnCurrencies()
    {
        var manager = new PinnedConversionManager([new(1m, "usd", "inr")]);

        Assert.True(manager.Contains(new PinnedConversion(1m, "USD", "INR")));
        Assert.False(manager.Contains(new PinnedConversion(2m, "usd", "inr")));
        Assert.False(manager.Contains(new PinnedConversion(1m, "usd", "eur")));
    }

    [Fact]
    public void ParsePinsJson_ReadsValidEntriesAndSkipsInvalid()
    {
        string json = """
            [
              { "amount": 34, "from": "btc", "to": "aed" },
              { "amount": 1, "from": "usd" },
              { "from": "eur", "to": "gbp" },
              { "amount": 100, "from": "USD", "to": "INR" }
            ]
            """;

        List<PinnedConversion> pins = PinnedConversionManager.ParsePinsJson(json);

        Assert.Equal(2, pins.Count);
        Assert.Contains(pins, p => p.Equals(new PinnedConversion(34m, "btc", "aed")));
        Assert.Contains(pins, p => p.Equals(new PinnedConversion(100m, "usd", "inr")));
    }

    [Fact]
    public void ParsePinsJson_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(PinnedConversionManager.ParsePinsJson(""));
        Assert.Empty(PinnedConversionManager.ParsePinsJson("   "));
        Assert.Empty(PinnedConversionManager.ParsePinsJson("[]"));
    }

    [Fact]
    public void GetPinsJson_RoundTripsThroughParse()
    {
        var manager = new PinnedConversionManager(
        [
            new(34m, "btc", "aed"),
            new(100m, "usd", "inr"),
        ]);

        string json = manager.GetPinsJson();
        List<PinnedConversion> parsed = PinnedConversionManager.ParsePinsJson(json);

        Assert.Equal(2, parsed.Count);
        Assert.Contains(parsed, p => p.Equals(new PinnedConversion(34m, "btc", "aed")));
        Assert.Contains(parsed, p => p.Equals(new PinnedConversion(100m, "usd", "inr")));
    }

    [Fact]
    public void PinnedConversion_Equality_IgnoresCurrencyCase()
    {
        var a = new PinnedConversion(1m, "Usd", "Inr");
        var b = new PinnedConversion(1m, "usd", "inr");
        var c = new PinnedConversion(1m, "usd", "eur");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
