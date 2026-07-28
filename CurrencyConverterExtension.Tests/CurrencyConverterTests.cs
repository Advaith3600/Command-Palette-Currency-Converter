using System.Globalization;
using System.Net;
using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using CurrencyConverterExtension.Tests.Fakes;

namespace CurrencyConverterExtension.Tests;

public class CurrencyConverterTests
{
    private static string DefaultUsdRatesJson() =>
        """{"date":"2024-01-01","usd":{"inr":80,"eur":0.9,"gbp":0.8}}""";

    private static MockHttpMessageHandler CreateDefaultHandler(Action? onRequest = null)
    {
        return new MockHttpMessageHandler(request =>
        {
            onRequest?.Invoke();
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/currencies/usd", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/usd", StringComparison.OrdinalIgnoreCase))
            {
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, DefaultUsdRatesJson());
            }

            if (path.Contains("/currencies/eur", StringComparison.OrdinalIgnoreCase))
            {
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"date":"2024-01-01","eur":{"usd":1.1,"inr":88}}""");
            }

            return MockHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
    }

    private static CurrencyConverter CreateConverter(
        FakeConversionSettings? settings = null,
        AliasManager? aliasManager = null,
        HttpMessageHandler? handler = null)
    {
        settings ??= new FakeConversionSettings();
        aliasManager ??= new AliasManager(new Dictionary<string, string> { ["$"] = "usd", ["euro"] = "eur" });
        handler ??= CreateDefaultHandler();
        return new CurrencyConverter(settings, aliasManager, handler);
    }

    [Fact]
    public void GetConversionResults_SinglePair_ReturnsExpandedTitle()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;

        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 1 });

        var results = converter.GetConversionResults(100m, "usd", "inr");

        Assert.Single(results);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("USD", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("=", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConversionResults_CompactOutputStyle_OmitsEquals()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 0 });

        var results = converter.GetConversionResults(100m, "usd", "inr");

        Assert.Single(results);
        Assert.DoesNotContain("=", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConversionResults_ResolvesAliases()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = converter.GetConversionResults(100m, "$", "euro");

        Assert.Single(results);
        Assert.Contains("USD", results[0].Subtitle!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EUR", results[0].Subtitle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetConversionResults_SameCurrency_ReturnsEmpty()
    {
        using var converter = CreateConverter();

        var results = converter.GetConversionResults(100m, "usd", "usd");

        Assert.Empty(results);
    }

    [Fact]
    public void GetConversionResults_UsesCacheOnSecondCall()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        int requests = 0;
        var handler = CreateDefaultHandler(() => requests++);
        using var converter = CreateConverter(handler: handler);

        _ = converter.GetConversionResults(100m, "usd", "inr");
        _ = converter.GetConversionResults(200m, "usd", "eur");

        Assert.Equal(1, requests);
    }

    [Fact]
    public void GetConversionResults_NotFound_ReturnsErrorItem()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
        using var converter = CreateConverter(handler: handler);

        var results = converter.GetConversionResults(100m, "zzz", "usd");

        Assert.Single(results);
        Assert.Contains("ZZZ", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("not a valid currency", results[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetConversionResults_Non404ThenFallbackSucceeds()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        int calls = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return MockHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}");
            }

            return MockHttpMessageHandler.Json(HttpStatusCode.OK, DefaultUsdRatesJson());
        });
        using var converter = CreateConverter(handler: handler);

        var results = converter.GetConversionResults(100m, "usd", "inr");

        Assert.Single(results);
        Assert.Equal(2, calls);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void GetConversionResults_BothCurrenciesSet_ReturnsSingleResult()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = converter.GetConversionResults(50m, "usd", "eur");

        Assert.Single(results);
    }

    [Fact]
    public void GetConversionResults_EmptyTo_ConvertsToLocalAndList()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "gbp",
            Currencies = ["eur", "inr"],
            ConversionDirection = 0,
        };

        // Need rates for usd that include gbp/eur/inr
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"date":"2024-01-01","usd":{"gbp":0.8,"eur":0.9,"inr":80}}"""));
        using var converter = CreateConverter(settings, handler: handler);

        var results = converter.GetConversionResults(100m, "usd", "");

        // local + eur + inr (direction 0 adds local first)
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void GetConversionResults_EmptyFrom_Direction0_ProducesBothWays()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "usd",
            Currencies = ["eur"],
            ConversionDirection = 0,
        };

        var handler = new MockHttpMessageHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/usd", StringComparison.OrdinalIgnoreCase))
            {
                return MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"date":"2024-01-01","usd":{"eur":0.9}}""");
            }

            return MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"date":"2024-01-01","eur":{"usd":1.1}}""");
        });
        using var converter = CreateConverter(settings, handler: handler);

        var results = converter.GetConversionResults(100m, "", "");

        // usd->eur and eur->usd
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void CalculateConvertedAmount_UsesCurrencyDecimalDigits()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        (decimal amount, int precision) = converter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(2, precision);
        Assert.Equal(123.45m, amount);
    }

    [Fact]
    public void CalculateConvertedAmount_IncreasesPrecisionForSmallValues()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        (decimal amount, int precision) = converter.CalculateConvertedAmount(1m, 0.00123m);

        Assert.Equal(4, precision);
        Assert.Equal(0.0012m, amount);
    }

    [Fact]
    public void ValidateConversionAPI_ThrowsWhenKeyMissingForPaidApi()
    {
        using var converter = CreateConverter(new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.ExchangeRateAPI,
            ConversionAPIKey = "",
        });

        Assert.Throws<Exception>(() => converter.ValidateConversionAPI());
    }
}