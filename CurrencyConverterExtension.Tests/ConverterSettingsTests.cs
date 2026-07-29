using System.Text.Json;
using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Tests.Fakes;

namespace CurrencyConverterExtension.Tests;

public class ConverterSettingsTests
{
    [Fact]
    public void GetConversionLink_DefaultApi_UsesTemplate()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.Default };
        var converterSettings = new ConverterSettings(settings) { ConversionDate = "latest" };

        string link = converterSettings.GetConversionLink("usd", "inr");

        Assert.Equal("https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/usd.min.json", link);
    }

    [Fact]
    public void GetConversionFallbackLink_DefaultApi_UsesTemplate()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.Default };
        var converterSettings = new ConverterSettings(settings) { ConversionDate = "latest" };

        string link = converterSettings.GetConversionFallbackLink("usd", "inr");

        Assert.Equal("https://latest.currency-api.pages.dev/v1/currencies/usd.min.json", link);
    }

    [Fact]
    public void GetConversionLink_ExchangeRateApi_IncludesApiKey()
    {
        var settings = new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.ExchangeRateAPI,
            ConversionAPIKey = "secret-key",
        };
        var converterSettings = new ConverterSettings(settings) { ConversionDate = "latest" };

        string link = converterSettings.GetConversionLink("usd", "inr");

        Assert.Equal("https://v6.exchangerate-api.com/v6/secret-key/latest/usd", link);
    }

    [Fact]
    public void GetConversionLink_CurrencyApi_UppercasesFromCurrency()
    {
        var settings = new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.CurrencyAPI,
            ConversionAPIKey = "secret-key",
        };
        var converterSettings = new ConverterSettings(settings) { ConversionDate = "latest" };

        string link = converterSettings.GetConversionLink("usd", "inr");

        Assert.Equal("https://api.currencyapi.com/v3/latest?apikey=secret-key&base_currency=USD", link);
    }

    [Fact]
    public void ValidateConversionAPI_Default_DoesNotRequireKey()
    {
        var settings = new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.Default,
            ConversionAPIKey = "",
        };
        var converterSettings = new ConverterSettings(settings);

        converterSettings.ValidateConversionAPI();
    }

    [Fact]
    public void ValidateConversionAPI_ExchangeRateApi_RequiresKey()
    {
        var settings = new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.ExchangeRateAPI,
            ConversionAPIKey = "",
        };
        var converterSettings = new ConverterSettings(settings);

        var ex = Assert.Throws<InvalidOperationException>(() => converterSettings.ValidateConversionAPI());
        Assert.Equal("Conversion API Key is not provided", ex.Message);
    }

    [Fact]
    public void GetRootJsonElementFor_DefaultApi_ReturnsFromCurrencyObject()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.Default };
        var converterSettings = new ConverterSettings(settings);
        const string json = """{"date":"2024-01-01","usd":{"inr":83.1,"eur":0.92}}""";

        JsonElement root = converterSettings.GetRootJsonElementFor(json, "usd");

        Assert.True(root.TryGetProperty("inr", out _));
        Assert.True(root.TryGetProperty("eur", out _));
    }

    [Fact]
    public void GetRootJsonElementFor_ExchangeRateApi_ReturnsConversionRates()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.ExchangeRateAPI, ConversionAPIKey = "k" };
        var converterSettings = new ConverterSettings(settings);
        const string json = """{"conversion_rates":{"INR":83.1,"EUR":0.92}}""";

        JsonElement root = converterSettings.GetRootJsonElementFor(json, "usd");

        Assert.True(root.TryGetProperty("INR", out _));
    }

    [Fact]
    public void GetRootJsonElementFor_CurrencyApi_ReturnsData()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.CurrencyAPI, ConversionAPIKey = "k" };
        var converterSettings = new ConverterSettings(settings);
        const string json = """{"data":{"INR":{"code":"INR","value":83.1}}}""";

        JsonElement root = converterSettings.GetRootJsonElementFor(json, "usd");

        Assert.True(root.TryGetProperty("INR", out _));
    }

    [Fact]
    public void GetRateFor_DefaultApi_ReturnsNameAndValue()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.Default };
        var converterSettings = new ConverterSettings(settings);
        using var doc = JsonDocument.Parse("""{"inr":83.1}""");
        JsonProperty property = doc.RootElement.EnumerateObject().First();

        (string code, decimal rate) = converterSettings.GetRateFor(property);

        Assert.Equal("inr", code);
        Assert.Equal(83.1m, rate);
    }

    [Fact]
    public void GetRateFor_CurrencyApi_ReturnsCodeAndValue()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.CurrencyAPI, ConversionAPIKey = "k" };
        var converterSettings = new ConverterSettings(settings);
        using var doc = JsonDocument.Parse("""{"INR":{"code":"INR","value":83.1}}""");
        JsonProperty property = doc.RootElement.EnumerateObject().First();

        (string code, decimal rate) = converterSettings.GetRateFor(property);

        Assert.Equal("INR", code);
        Assert.Equal(83.1m, rate);
    }

    [Fact]
    public void GetRateFor_CurrencyApi_InvalidShape_Throws()
    {
        var settings = new FakeConversionSettings { ConversionAPI = (int)ConverterSettingsEnum.CurrencyAPI, ConversionAPIKey = "k" };
        var converterSettings = new ConverterSettings(settings);
        using var doc = JsonDocument.Parse("""{"INR":{"code":"INR"}}""");
        JsonProperty property = doc.RootElement.EnumerateObject().First();

        Assert.Throws<InvalidOperationException>(() => converterSettings.GetRateFor(property));
    }
}