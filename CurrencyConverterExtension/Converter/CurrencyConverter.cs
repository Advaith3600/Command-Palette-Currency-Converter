using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Converter;

internal sealed record ConversionOutcome(
    ListItem Item,
    bool IsSuccess,
    decimal Amount,
    string FromCurrency,
    string ToCurrency,
    string ToFormatted,
    decimal Rate = 0m,
    DateTime RateUpdatedAt = default);

internal class CaseInsensitiveTupleComparer : IEqualityComparer<(string From, string To)>
{
    public bool Equals((string From, string To) x, (string From, string To) y)
    {
        return string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.To, y.To, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string From, string To) obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.From),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.To));
    }
}

internal sealed partial class CurrencyConverter : IDisposable
{
    internal IConversionSettings _settings;
    internal ConverterSettings _converterSettings;
    internal AliasManager _aliasManager;

    private readonly ConcurrentDictionary<(string From, string To), (decimal Rate, DateTime Timestamp)> _conversionCache = new(new CaseInsensitiveTupleComparer());
    private readonly ConcurrentDictionary<string, Task> _inFlightByBase = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;

    internal CurrencyConverter(IConversionSettings settings, AliasManager aliasManager)
        : this(settings, aliasManager, new HttpClientHandler())
    {
    }

    internal CurrencyConverter(IConversionSettings settings, AliasManager aliasManager, HttpMessageHandler httpMessageHandler)
    {
        _settings = settings;
        _converterSettings = new(_settings);
        _aliasManager = aliasManager;
        _httpClient = new HttpClient(httpMessageHandler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<List<ConversionOutcome>> GetConversionOutcomesAsync(
        decimal amountToConvert,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        List<Task<ConversionOutcome?>> conversionTasks = [];

        if (string.IsNullOrEmpty(fromCurrency))
        {
            if (!string.IsNullOrEmpty(toCurrency))
            {
                conversionTasks.Add(GetConversionAsync(amountToConvert, _settings.LocalCurrency, toCurrency, cancellationToken));
            }
            else
            {
                foreach (string currency in _settings.Currencies)
                {
                    conversionTasks.Add(GetConversionAsync(amountToConvert, _settings.LocalCurrency, currency, cancellationToken));
                }

                foreach (string currency in _settings.Currencies)
                {
                    conversionTasks.Add(GetConversionAsync(amountToConvert, currency, _settings.LocalCurrency, cancellationToken));
                }
            }
        }
        else if (string.IsNullOrEmpty(toCurrency))
        {
            conversionTasks.Add(GetConversionAsync(amountToConvert, fromCurrency, _settings.LocalCurrency, cancellationToken));

            foreach (string currency in _settings.Currencies)
            {
                conversionTasks.Add(GetConversionAsync(amountToConvert, fromCurrency, currency, cancellationToken));
            }
        }
        else
        {
            conversionTasks.Add(GetConversionAsync(amountToConvert, fromCurrency, toCurrency, cancellationToken));
        }

        ConversionOutcome?[] results = await Task.WhenAll(conversionTasks).ConfigureAwait(false);
        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    private async Task<ConversionOutcome?> GetConversionAsync(
        decimal amountToConvert,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken)
    {
        fromCurrency = GetCurrencyFromAlias(fromCurrency.ToLowerInvariant());
        toCurrency = GetCurrencyFromAlias(toCurrency.ToLowerInvariant());

        if (fromCurrency == toCurrency || string.IsNullOrEmpty(fromCurrency) || string.IsNullOrEmpty(toCurrency))
        {
            return null;
        }

        try
        {
            (decimal conversionRate, DateTime rateUpdatedAt) = await GetConversionRateAsync(fromCurrency, toCurrency, cancellationToken).ConfigureAwait(false);
            (decimal convertedAmount, int precision) = CalculateConvertedAmount(amountToConvert, conversionRate);

            string fromFormatted = amountToConvert.ToString("N", CultureInfo.CurrentCulture);
            string toFormatted = (amountToConvert < 0 ? convertedAmount * -1 : convertedAmount).ToString($"N{precision}", CultureInfo.CurrentCulture);

            string fromCode = fromCurrency.ToUpperInvariant();
            string toCode = toCurrency.ToUpperInvariant();

            ListItem item = new(CreateCopyCommand(toFormatted))
            {
                Title = $"{fromFormatted} {fromCode} → {toFormatted} {toCode}",
                Subtitle = string.Empty,
                Icon = CurrencyIconManager.For(toCurrency),
                Tags =
                [
                    new Tag(fromCode),
                    new Tag(toCode),
                ],
                Details = CreateConversionDetails(
                    fromFormatted,
                    fromCode,
                    toFormatted,
                    toCode,
                    conversionRate,
                    rateUpdatedAt),
            };

            return new ConversionOutcome(item, true, amountToConvert, fromCurrency, toCurrency, toFormatted, conversionRate, rateUpdatedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ConversionOutcome(
                CreateErrorItem("Unable to reach the conversion service", "Press enter or click to open the currencies list"),
                false,
                amountToConvert,
                fromCurrency,
                toCurrency,
                string.Empty);
        }
        catch (JsonException)
        {
            return new ConversionOutcome(
                CreateErrorItem("Received an invalid response from the conversion service", "Press enter or click to open the currencies list"),
                false,
                amountToConvert,
                fromCurrency,
                toCurrency,
                string.Empty);
        }
        catch (InvalidOperationException e)
        {
            return new ConversionOutcome(
                CreateErrorItem(e.Message, "Press enter or click to open the currencies list"),
                false,
                amountToConvert,
                fromCurrency,
                toCurrency,
                string.Empty);
        }
        catch (Exception)
        {
            return new ConversionOutcome(
                CreateErrorItem("Something went wrong while converting currencies", "Press enter or click to open the currencies list"),
                false,
                amountToConvert,
                fromCurrency,
                toCurrency,
                string.Empty);
        }
    }

    internal static CopyTextCommand CreateCopyCommand(string text) =>
        new(text)
        {
            Result = CommandResult.ShowToast(new ToastArgs()
            {
                Message = "Copied to clipboard",
                Result = CommandResult.Hide()
            })
        };

    internal static Details CreateConversionDetails(
        string fromFormatted,
        string fromCode,
        string toFormatted,
        string toCode,
        decimal rate,
        DateTime rateUpdatedAt,
        params string[] extraTags)
    {
        string unitRate = FormatRate(rate);
        string inverseRate = rate == 0m ? "—" : FormatRate(1m / rate);
        string updatedAt = FormatRateUpdatedAt(rateUpdatedAt);

        Tag[] tags =
        [
            ..extraTags.Select(t => new Tag(t)),
            new Tag(fromCode),
            new Tag(toCode),
        ];

        return new Details
        {
            Title = $"{toFormatted} {toCode}",
            HeroImage = CurrencyIconManager.For(toCode),
            Body = $"**{fromFormatted} {fromCode}** → **{toFormatted} {toCode}**",
            Metadata =
            [
                new DetailsElement
                {
                    Key = "Unit rate",
                    Data = new DetailsLink { Text = $"1 {fromCode} = {unitRate} {toCode}" },
                },
                new DetailsElement
                {
                    Key = "Inverse rate",
                    Data = new DetailsLink { Text = $"1 {toCode} = {inverseRate} {fromCode}" },
                },
                new DetailsElement
                {
                    Key = "Tags",
                    Data = new DetailsTags { Tags = tags },
                },
                new DetailsElement
                {
                    Key = "Updated at",
                    Data = new DetailsLink { Text = updatedAt },
                },
            ],
        };
    }

    internal static string FormatRateUpdatedAt(DateTime rateUpdatedAtUtc)
    {
        if (rateUpdatedAtUtc == default)
        {
            return "—";
        }

        return rateUpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    internal static string FormatRate(decimal rate)
    {
        decimal absRate = Math.Abs(rate);
        int precision = Math.Max(CultureInfo.CurrentCulture.NumberFormat.CurrencyDecimalDigits, 4);

        if (absRate > 0m && absRate < 1m)
        {
            string rawStr = absRate.ToString("F10", CultureInfo.InvariantCulture);
            int decimalPointIndex = rawStr.IndexOf('.');
            if (decimalPointIndex != -1)
            {
                int numberOfZeros = rawStr.Substring(decimalPointIndex + 1).TakeWhile(c => c == '0').Count();
                precision = Math.Max(precision, numberOfZeros + 4);
            }
        }

        return Math.Round(rate, precision).ToString($"N{precision}", CultureInfo.CurrentCulture);
    }

    private ListItem CreateErrorItem(string title, string subtitle) =>
        new(new OpenUrlCommand(_converterSettings.GetHelperLink()))
        {
            Title = title,
            Subtitle = subtitle,
            Icon = IconManager.WarningIcon,
        };

    /// <summary>Removes all cached rates whose base (from) currency matches.</summary>
    internal void InvalidateCacheForFromCurrency(string fromCurrency)
    {
        string resolved = GetCurrencyFromAlias(fromCurrency.Trim().ToLowerInvariant());
        List<(string From, string To)> toRemove = [];

        foreach ((string From, string To) key in _conversionCache.Keys)
        {
            if (string.Equals(key.From, resolved, StringComparison.OrdinalIgnoreCase))
            {
                toRemove.Add(key);
            }
        }

        foreach ((string From, string To) key in toRemove)
        {
            _conversionCache.TryRemove(key, out _);
        }
    }

    private async Task<(decimal Rate, DateTime UpdatedAt)> GetConversionRateAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken)
    {
        var cacheKey = (fromCurrency, toCurrency);

        if (TryGetFreshCachedRate(cacheKey, out var cached))
        {
            return cached;
        }

        // Coalesce concurrent misses for the same base currency into one HTTP fetch.
        Task populateTask = _inFlightByBase.GetOrAdd(
            fromCurrency,
            static (baseCurrency, self) => self.PopulateCacheForBaseAsync(baseCurrency),
            this);

        try
        {
            await populateTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (populateTask.IsCompleted)
            {
                _inFlightByBase.TryRemove(new KeyValuePair<string, Task>(fromCurrency, populateTask));
            }
        }

        if (TryGetFreshCachedRate(cacheKey, out cached))
        {
            return cached;
        }

        // Populate finished without this pair (invalid target) or faulted — surface the fault.
        if (populateTask.IsFaulted)
        {
            await populateTask.ConfigureAwait(false);
        }

        throw new InvalidOperationException($"{toCurrency.ToUpperInvariant()} is not a valid currency");
    }

    private bool TryGetFreshCachedRate(
        (string From, string To) cacheKey,
        out (decimal Rate, DateTime UpdatedAt) cached)
    {
        if (_conversionCache.TryGetValue(cacheKey, out var directCacheData))
        {
            if (directCacheData.Timestamp > DateTime.UtcNow.AddHours(-_settings.ConversionCacheDuration))
            {
                cached = (directCacheData.Rate, directCacheData.Timestamp);
                return true;
            }

            // Lazy eviction so the dictionary does not retain stale entries forever.
            _conversionCache.TryRemove(cacheKey, out _);
        }

        cached = default;
        return false;
    }

    private async Task PopulateCacheForBaseAsync(string fromCurrency)
    {
        // Shared in-flight work is not tied to a single caller's token; waiters use WaitAsync.
        string url = _converterSettings.GetConversionLink(fromCurrency, fromCurrency);
        using HttpResponseMessage response = await GetWithFallbackAsync(url, fromCurrency, fromCurrency, CancellationToken.None)
            .ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);

        DateTime fetchedAt = DateTime.UtcNow;
        JsonElement fromCurrencyElement = _converterSettings.GetRootJsonElementFor(content, fromCurrency);
        foreach (JsonProperty property in fromCurrencyElement.EnumerateObject())
        {
            (string targetCurrency, decimal rate) = _converterSettings.GetRateFor(property);
            _conversionCache[(fromCurrency, targetCurrency)] = (rate, fetchedAt);
        }
    }

    private async Task<HttpResponseMessage> GetWithFallbackAsync(
        string url,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        System.Net.HttpStatusCode statusCode = response.StatusCode;
        response.Dispose();

        if (statusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency");
        }

        // Do not retry auth/throttle failures, or when the fallback URL is identical (doubles metered use).
        if (statusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("Something went wrong while fetching the conversion rate");
        }

        string fallbackUrl = _converterSettings.GetConversionFallbackLink(fromCurrency, toCurrency);
        if (string.Equals(url, fallbackUrl, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Something went wrong while fetching the conversion rate");
        }

        HttpResponseMessage fallbackResponse = await _httpClient.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);

        if (fallbackResponse.IsSuccessStatusCode)
        {
            return fallbackResponse;
        }

        statusCode = fallbackResponse.StatusCode;
        fallbackResponse.Dispose();

        throw statusCode == System.Net.HttpStatusCode.NotFound
            ? new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency")
            : new InvalidOperationException("Something went wrong while fetching the conversion rate");
    }

    private string GetCurrencyFromAlias(string currency)
    {
        if (_aliasManager.HasAlias(currency))
        {
            return _aliasManager.GetAlias(currency) ?? currency;
        }

        return currency;
    }

    internal static (decimal ConvertedAmount, int Precision) CalculateConvertedAmount(decimal amountToConvert, decimal conversionRate)
    {
        int precision = CultureInfo.CurrentCulture.NumberFormat.CurrencyDecimalDigits;
        decimal rawConvertedAmount = Math.Abs(amountToConvert * conversionRate);
        decimal convertedAmount = Math.Round(rawConvertedAmount, precision);

        if (rawConvertedAmount < 1)
        {
            string rawStr = rawConvertedAmount.ToString("F10", CultureInfo.InvariantCulture);
            int decimalPointIndex = rawStr.IndexOf('.');
            if (decimalPointIndex != -1)
            {
                int numberOfZeros = rawStr.Substring(decimalPointIndex + 1).TakeWhile(c => c == '0').Count();
                precision += numberOfZeros;
                convertedAmount = Math.Round(rawConvertedAmount, precision);
            }
        }

        return (convertedAmount, precision);
    }

    internal void ValidateConversionAPI() => _converterSettings.ValidateConversionAPI();

    internal string GetHelperLink() => _converterSettings.GetHelperLink();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
    }
}
