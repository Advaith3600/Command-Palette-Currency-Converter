using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions.Toolkit;

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
        
        HttpClientHandler handler = new()
        {
            UseDefaultCredentials = true,
            PreAuthenticate = true
        };
        _httpClient = new HttpClient(handler);
    }

    public List<ListItem> GetConversionResults(decimal amountToConvert, string fromCurrency, string toCurrency)
    {
        List<(int index, string fromCurrency, Task<ListItem?> task)> conversionTasks = [];
        int index = 0;

        if (string.IsNullOrEmpty(fromCurrency))
        {
            foreach (string currency in _settings.Currencies)
            {
                if (_settings.ConversionDirection == 0)
                {
                    conversionTasks.Add((index++, _settings.LocalCurrency, Task.Run(() => GetConversion(amountToConvert, _settings.LocalCurrency, currency))));
                }
                else
                {
                    conversionTasks.Add((index++, currency, Task.Run(() => GetConversion(amountToConvert, currency, _settings.LocalCurrency))));
                }
            }

            foreach (string currency in _settings.Currencies)
            {
                if (_settings.ConversionDirection == 0)
                {
                    conversionTasks.Add((index++, currency, Task.Run(() => GetConversion(amountToConvert, currency, _settings.LocalCurrency))));
                }
                else
                {
                    conversionTasks.Add((index++, _settings.LocalCurrency, Task.Run(() => GetConversion(amountToConvert, _settings.LocalCurrency, currency))));
                }
            }
        }
        else if (string.IsNullOrEmpty(toCurrency))
        {
            if (_settings.ConversionDirection == 0)
            {
                conversionTasks.Add((index++, fromCurrency, Task.Run(() => GetConversion(amountToConvert, fromCurrency, _settings.LocalCurrency))));
            }

            foreach (string currency in _settings.Currencies)
            {
                conversionTasks.Add((index++, fromCurrency, Task.Run(() => GetConversion(amountToConvert, fromCurrency, currency))));
            }

            if (_settings.ConversionDirection == 1)
            {
                conversionTasks.Add((index++, fromCurrency, Task.Run(() => GetConversion(amountToConvert, fromCurrency, _settings.LocalCurrency))));
            }
        }
        else
        {
            conversionTasks.Add((index++, fromCurrency, Task.Run(() => GetConversion(amountToConvert, fromCurrency, toCurrency))));
        }

        var groupedTasks = conversionTasks.GroupBy(t => t.fromCurrency);

        var results = new ListItem?[conversionTasks.Count];

        Parallel.ForEach(groupedTasks, group =>
        {
            foreach (var task in group)
            {
                task.task.Wait();
                results[task.index] = task.task.Result;
            }
        });

        return results.Where(r => r != null).ToList();
    }

    private ListItem? GetConversion(decimal amountToConvert, string fromCurrency, string toCurrency)
    {
        fromCurrency = GetCurrencyFromAlias(fromCurrency.ToLower());
        toCurrency = GetCurrencyFromAlias(toCurrency.ToLower());

        if (fromCurrency == toCurrency || string.IsNullOrEmpty(fromCurrency) || string.IsNullOrEmpty(toCurrency))
        {
            return null;
        }

        try
        {
            decimal conversionRate = GetConversionRateSync(fromCurrency, toCurrency);
            (decimal convertedAmount, int precision) = CalculateConvertedAmount(amountToConvert, conversionRate);

            string fromFormatted = amountToConvert.ToString("N", CultureInfo.CurrentCulture);
            string toFormatted = (amountToConvert < 0 ? convertedAmount * -1 : convertedAmount).ToString($"N{precision}", CultureInfo.CurrentCulture);

            string compressedOutput = $"{toFormatted} {toCurrency.ToUpper()}";
            string expandedOutput = $"{fromFormatted} {fromCurrency.ToUpper()} = {toFormatted} {toCurrency.ToUpper()}";

            return new ListItem(new CopyTextCommand(toFormatted))
            {
                Title = _settings.OutputStyle == 0 ? compressedOutput : expandedOutput,
                Subtitle = $"Currency conversion from {fromCurrency.ToUpper()} to {toCurrency.ToUpper()}",
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

    private decimal GetConversionRateSync(string fromCurrency, string toCurrency)
    {
        var cacheKey = (fromCurrency, toCurrency);

        if (_conversionCache.TryGetValue(cacheKey, out var directCacheData) &&
            directCacheData.Timestamp > DateTime.Now.AddHours(-_settings.ConversionCacheDuration))
        {
            return directCacheData.Rate;
        }

        string url = _converterSettings.GetConversionLink(fromCurrency, toCurrency);
        HttpResponseMessage response = _httpClient.GetAsync(url).Result;

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new Exception($"{fromCurrency.ToUpper()} is not a valid currency");
            }
            else
            {
                string fallbackUrl = _converterSettings.GetConversionFallbackLink(fromCurrency, toCurrency);
                response = _httpClient.GetAsync(fallbackUrl).Result;

                if (!response.IsSuccessStatusCode)
                {
                    throw response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? new Exception($"{fromCurrency.ToUpper()} is not a valid currency")
                        : new Exception("Something went wrong while fetching the conversion rate");
                }
            }
        }

        string content = response.Content.ReadAsStringAsync().Result;
        decimal conversionRate;

        JsonElement fromCurrencyElement = _converterSettings.GetRootJsonElementFor(content, fromCurrency);
        foreach (JsonProperty property in fromCurrencyElement.EnumerateObject())
        {
            (string targetCurrency, decimal rate) = _converterSettings.GetRateFor(property);
            _conversionCache[(fromCurrency, targetCurrency)] = (rate, DateTime.Now);
        }
        if (!_conversionCache.TryGetValue((fromCurrency, toCurrency), out var cacheOutput))
        {
            throw new Exception($"{toCurrency.ToUpper()} is not a valid currency");
        }
        conversionRate = cacheOutput.Rate;

        return conversionRate;
    }

    private string GetCurrencyFromAlias(string currency)
    {
        if (_aliasManager.HasAlias(currency))
        {
            return _aliasManager.GetAlias(currency);
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
