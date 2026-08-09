using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using CurrencyConverterExtension.Tests.Fakes;
using System.Globalization;
using System.Net;

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
    public async Task GetConversionOutcomes_SinglePair_ReturnsFullTitle()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = culture;

        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("INR", results[0].Item.Title, StringComparison.Ordinal);
        Assert.Contains("USD", results[0].Item.Title, StringComparison.Ordinal);
        Assert.Contains("\u2192", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_ResolvesAliases()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(100m, "$", "euro", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("USD", results[0].Item.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EUR", results[0].Item.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\u2192", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_SameCurrency_ReturnsEmpty()
    {
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "usd", TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetConversionOutcomes_UsesCacheOnSecondCall()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        int requests = 0;
        var handler = CreateDefaultHandler(() => requests++);
        using var converter = CreateConverter(handler: handler);

        _ = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);
        _ = await converter.GetConversionOutcomesAsync(200m, "usd", "eur", TestContext.Current.CancellationToken);

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetConversionOutcomes_Success_IncludesConversionDetails()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        var details = results[0].Item.Details;
        Assert.NotNull(details);
        Assert.Contains("INR", details.Title, StringComparison.Ordinal);
        Assert.Contains("USD", details.Body, StringComparison.Ordinal);
        Assert.Contains("INR", details.Body, StringComparison.Ordinal);

        var metadataTexts = details.Metadata!
            .Select(m => m.Data)
            .OfType<Microsoft.CommandPalette.Extensions.Toolkit.DetailsLink>()
            .Select(l => l.Text)
            .ToList();

        Assert.Equal(3, metadataTexts.Count);
        Assert.Contains(metadataTexts, t => t is not null && t.Contains("1 USD", StringComparison.Ordinal));
        Assert.Contains(metadataTexts, t => t is not null && t.Contains("1 INR", StringComparison.Ordinal));
        Assert.Contains(metadataTexts, t => t is not null && t.Length > 0 && t != "â€”");

        var detailsTags = details.Metadata!
            .Select(m => m.Data)
            .OfType<Microsoft.CommandPalette.Extensions.Toolkit.DetailsTags>()
            .Single();
        var tagTexts = detailsTags.Tags!.Select(t => t.Text).ToList();
        Assert.Equal(["USD", "INR"], tagTexts);

        Assert.Contains(details.Metadata!, m => m.Key == "Updated at");
    }

    [Fact]
    public async Task GetConversionOutcomes_NotFound_ReturnsErrorItem()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
        using var converter = CreateConverter(handler: handler);

        var results = await converter.GetConversionOutcomesAsync(100m, "zzz", "usd", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("ZZZ", results[0].Item.Title, StringComparison.Ordinal);
        Assert.Contains("not a valid currency", results[0].Item.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Null(results[0].Item.Details);
    }

    [Fact]
    public void FormatRate_SmallValues_UsesExtraPrecision()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        string formatted = CurrencyConverter.FormatRate(0.00001234m);

        Assert.Contains("0.000012", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_Non404ThenFallbackSucceeds()
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

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal(2, calls);
        Assert.Contains("INR", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_BothCurrenciesSet_ReturnsSingleResult()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(50m, "usd", "eur", TestContext.Current.CancellationToken);

        Assert.Single(results);
    }

    [Fact]
    public async Task GetConversionOutcomes_EmptyTo_ConvertsToLocalAndList()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "gbp",
            Currencies = ["eur", "inr"],
        };

        // Need rates for usd that include gbp/eur/inr
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.Json(HttpStatusCode.OK, """{"date":"2024-01-01","usd":{"gbp":0.8,"eur":0.9,"inr":80}}"""));
        using var converter = CreateConverter(settings, handler: handler);

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "", TestContext.Current.CancellationToken);

        // local + eur + inr (local conversion first)
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GetConversionOutcomes_EmptyFrom_ProducesBothWays()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "usd",
            Currencies = ["eur"],
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

        var results = await converter.GetConversionOutcomesAsync(100m, "", "", TestContext.Current.CancellationToken);

        // usd->eur and eur->usd
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetConversionOutcomes_EmptyFromWithTo_ConvertsLocalToTarget()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var settings = new FakeConversionSettings
        {
            LocalCurrency = "usd",
            Currencies = ["eur", "gbp"],
        };

        using var converter = CreateConverter(settings);

        var results = await converter.GetConversionOutcomesAsync(100m, "", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Contains("INR", results[0].Item.Title, StringComparison.Ordinal);
        Assert.Contains("USD", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateConvertedAmount_UsesCurrencyDecimalDigits()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        (decimal amount, int precision) = CurrencyConverter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(2, precision);
        Assert.Equal(123.45m, amount);
    }

    [Fact]
    public void CalculateConvertedAmount_IncreasesPrecisionForSmallValues()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        (decimal amount, int precision) = CurrencyConverter.CalculateConvertedAmount(1m, 0.00123m);

        Assert.Equal(4, precision);
        Assert.Equal(0.0012m, amount);
    }

    [Fact]
    public async Task GetConversionOutcomes_DeDE_OutputUsesCommaDecimalAndDotGroupSeparator()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(1000m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // de-DE formats 80000 as "80.000,00" (dot group sep, comma decimal sep)
        Assert.Contains("80.000,00", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_JaJP_OutputUsesZeroDecimalDigits()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // ja-JP has 0 CurrencyDecimalDigits, so 8000 with no decimal portion
        Assert.Contains("8,000 INR", results[0].Item.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("8,000.00", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConversionOutcomes_DeDE_FullTitleFormatsCorrectly()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        using var converter = CreateConverter();

        var results = await converter.GetConversionOutcomesAsync(1000m, "usd", "inr", TestContext.Current.CancellationToken);

        Assert.Single(results);
        // Both sides should use de-DE formatting
        Assert.Contains("1.000,00", results[0].Item.Title, StringComparison.Ordinal);
        Assert.Contains("80.000,00", results[0].Item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateConvertedAmount_JaJP_ReturnsZeroPrecision()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
        (decimal amount, int precision) = CurrencyConverter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(0, precision);
        Assert.Equal(123m, amount);
    }

    [Fact]
    public void CalculateConvertedAmount_DeDE_ReturnsTwoDecimalPrecision()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        (decimal amount, int precision) = CurrencyConverter.CalculateConvertedAmount(100m, 1.2345m);

        Assert.Equal(2, precision);
        Assert.Equal(123.45m, amount);
    }

    [Fact]
    public void ValidateConversionAPI_ThrowsWhenKeyMissingForPaidApi()
    {
        using var converter = CreateConverter(new FakeConversionSettings
        {
            ConversionAPI = (int)ConverterSettingsApi.ExchangeRateAPI,
            ConversionAPIKey = "",
        });

        Assert.Throws<InvalidOperationException>(() => converter.ValidateConversionAPI());
    }

    [Fact]
    public async Task InvalidateCacheForFromCurrency_ForcesRefetchForThatBaseOnly()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        int requests = 0;
        var handler = CreateDefaultHandler(() => requests++);
        using var converter = CreateConverter(handler: handler);

        _ = await converter.GetConversionOutcomesAsync(100m, "usd", "inr", TestContext.Current.CancellationToken);
        _ = await converter.GetConversionOutcomesAsync(100m, "eur", "usd", TestContext.Current.CancellationToken);
        Assert.Equal(2, requests);

        converter.InvalidateCacheForFromCurrency("usd");

        _ = await converter.GetConversionOutcomesAsync(100m, "usd", "eur", TestContext.Current.CancellationToken);
        Assert.Equal(3, requests);

        // EUR base still warm — no extra request.
        _ = await converter.GetConversionOutcomesAsync(50m, "eur", "inr", TestContext.Current.CancellationToken);
        Assert.Equal(3, requests);
    }

}


