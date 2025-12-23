using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Converter;

internal class CaseInsensitiveTupleComparer : IEqualityComparer<(string From, string To)>
{
    public bool Equals((string From, string To) x, (string From, string To) y)
    {
        return string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.To, y.To, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string From, string To) obj)
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.From) ^
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.To);
    }
}

internal sealed partial class CurrencyConverter : IDisposable
{
    internal SettingsManager _settings;
    internal ConverterSettings _converterSettings;
    internal AliasManager _aliasManager;

    private readonly ConcurrentDictionary<(string From, string To), (decimal Rate, DateTime Timestamp)> _conversionCache = new(new CaseInsensitiveTupleComparer());
    private readonly HttpClient _httpClient;

    internal CurrencyConverter(SettingsManager settings, AliasManager aliasManager)
    {
        _settings = settings;
        _converterSettings = new(_settings);
        _aliasManager = aliasManager;

        var proxy = WebRequest.DefaultWebProxy;
        HttpClientHandler handler = new()
        {
            Proxy = proxy,
            UseProxy = proxy != null,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        _httpClient = new HttpClient(handler);
    }

    public List<ListItem> GetConversionResults(decimal amountToConvert, string fromCurrency, string toCurrency)
    {
        List<(int index, Task<ListItem?> task)> conversionTasks = [];
        int index = 0;

        if (string.IsNullOrEmpty(fromCurrency))
        {
            foreach (string currency in _settings.Currencies)
            {
                if (_settings.ConversionDirection == 0)
                {
                    conversionTasks.Add((index++, GetConversionAsync(amountToConvert, _settings.LocalCurrency, currency)));
                }
                else
                {
                    conversionTasks.Add((index++, GetConversionAsync(amountToConvert, currency, _settings.LocalCurrency)));
                }
            }

            foreach (string currency in _settings.Currencies)
            {
                if (_settings.ConversionDirection == 0)
                {
                    conversionTasks.Add((index++, GetConversionAsync(amountToConvert, currency, _settings.LocalCurrency)));
                }
                else
                {
                    conversionTasks.Add((index++, GetConversionAsync(amountToConvert, _settings.LocalCurrency, currency)));
                }
            }
        }
        else if (string.IsNullOrEmpty(toCurrency))
        {
            if (_settings.ConversionDirection == 0)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, _settings.LocalCurrency)));
            }

            foreach (string currency in _settings.Currencies)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, currency)));
            }

            if (_settings.ConversionDirection == 1)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, _settings.LocalCurrency)));
            }
        }
        else
        {
            conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, toCurrency)));
        }

        Task.WhenAll(conversionTasks.Select(t => t.task)).GetAwaiter().GetResult();

        var results = new ListItem?[conversionTasks.Count];
        foreach (var task in conversionTasks)
        {
            results[task.index] = task.task.Result;
        }

        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    private async Task<ListItem?> GetConversionAsync(decimal amountToConvert, string fromCurrency, string toCurrency)
    {
        fromCurrency = GetCurrencyFromAlias(fromCurrency.ToLowerInvariant());
        toCurrency = GetCurrencyFromAlias(toCurrency.ToLowerInvariant());

        if (fromCurrency == toCurrency || string.IsNullOrEmpty(fromCurrency) || string.IsNullOrEmpty(toCurrency))
        {
            return null;
        }

        try
        {
            decimal conversionRate = await GetConversionRateAsync(fromCurrency, toCurrency).ConfigureAwait(false);
            (decimal convertedAmount, int precision) = CalculateConvertedAmount(amountToConvert, conversionRate);

            string fromFormatted = amountToConvert.ToString("N", CultureInfo.CurrentCulture);
            string toFormatted = (amountToConvert < 0 ? convertedAmount * -1 : convertedAmount).ToString($"N{precision}", CultureInfo.CurrentCulture);

            string compressedOutput = $"{toFormatted} {toCurrency.ToUpperInvariant()}";
            string expandedOutput = $"{fromFormatted} {fromCurrency.ToUpperInvariant()} = {toFormatted} {toCurrency.ToUpperInvariant()}";

            return new ListItem(new CopyTextCommand(toFormatted))
            {
                Title = _settings.OutputStyle == 0 ? compressedOutput : expandedOutput,
                Subtitle = $"Currency conversion from {fromCurrency.ToUpperInvariant()} to {toCurrency.ToUpperInvariant()}",
                Icon = IconManager.Icon,
            };
        }
        catch (Exception e)
        {
            return new(new OpenUrlCommand(_converterSettings.GetHelperLink()))
            {
                Title = e.Message,
                Subtitle = "Press enter or click to open the currencies list",
                Icon = IconManager.WarningIcon,
            };
        }
    }

    private async Task<decimal> GetConversionRateAsync(string fromCurrency, string toCurrency)
    {
        var cacheKey = (fromCurrency, toCurrency);

        if (_conversionCache.TryGetValue(cacheKey, out var directCacheData) &&
            directCacheData.Timestamp > DateTime.UtcNow.AddHours(-_settings.ConversionCacheDuration))
        {
            return directCacheData.Rate;
        }

        string url = _converterSettings.GetConversionLink(fromCurrency, toCurrency);
        HttpResponseMessage response = await _httpClient.GetAsync(url).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency");
                throw new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency");
            }
            else
            {
                string fallbackUrl = _converterSettings.GetConversionFallbackLink(fromCurrency, toCurrency);
                response = await _httpClient.GetAsync(fallbackUrl).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency")
                        : new InvalidOperationException("Something went wrong while fetching the conversion rate");
                }
            }
        }

        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        decimal conversionRate;

        JsonElement fromCurrencyElement = _converterSettings.GetRootJsonElementFor(content, fromCurrency);
        foreach (JsonProperty property in fromCurrencyElement.EnumerateObject())
        {
            (string targetCurrency, decimal rate) = _converterSettings.GetRateFor(property);
            _conversionCache[(fromCurrency, targetCurrency)] = (rate, DateTime.UtcNow);
        }
        if (!_conversionCache.TryGetValue((fromCurrency, toCurrency), out var cacheOutput))
        {
            throw new InvalidOperationException($"{toCurrency.ToUpperInvariant()} is not a valid currency");
        }
        conversionRate = cacheOutput.Rate;

        return conversionRate;
    }

    private string GetCurrencyFromAlias(string currency)
    {
        if (_aliasManager.HasAlias(currency))
        {
            return _aliasManager.GetAlias(currency) ?? currency;
        }

        return currency;
    }

    private (decimal ConvertedAmount, int Precision) CalculateConvertedAmount(decimal amountToConvert, decimal conversionRate)
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
    }
}
