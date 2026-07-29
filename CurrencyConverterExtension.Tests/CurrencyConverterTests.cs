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
    public async Task GetConversionResults_SinglePair_ReturnsExpandedTitle()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;

        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 1 });

        var results = await converter.GetConversionResultsAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("USD", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("=", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionResults_CompactOutputStyle_OmitsEquals()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 0 });

        var results = await converter.GetConversionResultsAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.DoesNotContain("=", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionResults_ResolvesAliases()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = await converter.GetConversionResultsAsync(100m, "$", "euro", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("USD", results[0].Subtitle!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EUR", results[0].Subtitle!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConversionResults_SameCurrency_ReturnsEmpty()
    {
        using var converter = CreateConverter();

        var results = await converter.GetConversionResultsAsync(100m, "usd", "usd", TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetConversionResults_UsesCacheOnSecondCall()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        int requests = 0;
        var handler = CreateDefaultHandler(() => requests++);
        using var converter = CreateConverter(handler: handler);

        _ = await converter.GetConversionResultsAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);
        _ = await converter.GetConversionResultsAsync(200m, "usd", "eur", TestContext.Current.CancellationToken);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetConversionResults_NotFound_ReturnsErrorItem()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
        using var converter = CreateConverter(handler: handler);

        var results = await converter.GetConversionResultsAsync(100m, "zzz", "usd", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("ZZZ", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("not a valid currency", results[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConversionResults_Non404ThenFallbackSucceeds()
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

        var results = await converter.GetConversionResultsAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(2, calls);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionResults_BothCurrenciesSet_ReturnsSingleResult()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = await converter.GetConversionResultsAsync(50m, "usd", "eur", TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    [Fact]
    public async Task GetConversionResults_EmptyTo_ConvertsToLocalAndList()
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

        var results = await converter.GetConversionResultsAsync(100m, "usd", "", TestContext.Current.CancellationToken);

        // local + eur + inr (direction 0 adds local first)
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GetConversionResults_EmptyFrom_Direction0_ProducesBothWays()
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

        var results = await converter.GetConversionResultsAsync(100m, "", "", TestContext.Current.CancellationToken);

        // usd->eur and eur->usd
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetConversionResults_EmptyFromWithTo_ConvertsLocalToTarget()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "usd",
            Currencies = ["eur", "gbp"],
            ConversionDirection = 0,
        };

        using var converter = CreateConverter(settings);

        var results = await converter.GetConversionResultsAsync(100m, "", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("INR", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("USD", results[0].Title, StringComparison.Ordinal);
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
    public async Task GetConversionResults_DeDE_OutputUsesCommaDecimalAndDotGroupSeparator()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 0 });

        var results = await converter.GetConversionResultsAsync(1000m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // de-DE formats 80000 as "80.000,00" (dot group sep, comma decimal sep)
        Assert.Contains("80.000,00", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionResults_JaJP_OutputUsesZeroDecimalDigits()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 0 });

        var results = await converter.GetConversionResultsAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // ja-JP has 0 CurrencyDecimalDigits, so 8000 with no decimal portion
        Assert.Contains("8,000 INR", results[0].Title, StringComparison.Ordinal);
        Assert.DoesNotContain("8,000.00", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionResults_DeDE_ExpandedOutputFormatsCorrectly()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using var converter = CreateConverter(new FakeConversionSettings { OutputStyle = 1 });

        var results = await converter.GetConversionResultsAsync(1000m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // Both sides should use de-DE formatting
        Assert.Contains("1.000,00", results[0].Title, StringComparison.Ordinal);
        Assert.Contains("80.000,00", results[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateConvertedAmount_JaJP_ReturnsZeroPrecision()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        using var converter = CreateConverter();

        (decimal amount, int precision) = converter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(0, precision);
        Assert.Equal(123m, amount);
    }

    [Fact]
    public void CalculateConvertedAmount_DeDE_ReturnsTwoDecimalPrecision()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using var converter = CreateConverter();

        (decimal amount, int precision) = converter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(2, precision);
        Assert.Equal(123.45m, amount);
    }

    [Fact]
    public void ValidateConversionAPI_ThrowsWhenKeyMissingForPaidApi()
    {
        using var converter = CreateConverter(new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsEnum.ExchangeRateAPI,
            ConversionAPIKey = "",
        });

        Assert.Throws<InvalidOperationException>(() => converter.ValidateConversionAPI());
    }
}
