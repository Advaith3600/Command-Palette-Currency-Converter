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
        _httpClient = new HttpClient(httpMessageHandler);
    }

    public async Task<List<ListItem>> GetConversionResultsAsync(
        decimal amountToConvert,
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        List<(int index, Task<ListItem?> task)> conversionTasks = [];
        int index = 0;

        if (string.IsNullOrEmpty(fromCurrency))
        {
            if (!string.IsNullOrEmpty(toCurrency))
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, _settings.LocalCurrency, toCurrency, cancellationToken)));
            }
            else
            {
                foreach (string currency in _settings.Currencies)
                {
                    if (_settings.ConversionDirection == 0)
                    {
                        conversionTasks.Add((index++, GetConversionAsync(amountToConvert, _settings.LocalCurrency, currency, cancellationToken)));
                    }
                    else
                    {
                        conversionTasks.Add((index++, GetConversionAsync(amountToConvert, currency, _settings.LocalCurrency, cancellationToken)));
                    }
                }

                foreach (string currency in _settings.Currencies)
                {
                    if (_settings.ConversionDirection == 0)
                    {
                        conversionTasks.Add((index++, GetConversionAsync(amountToConvert, currency, _settings.LocalCurrency, cancellationToken)));
                    }
                    else
                    {
                        conversionTasks.Add((index++, GetConversionAsync(amountToConvert, _settings.LocalCurrency, currency, cancellationToken)));
                    }
                }
            }
        }
        else if (string.IsNullOrEmpty(toCurrency))
        {
            if (_settings.ConversionDirection == 0)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, _settings.LocalCurrency, cancellationToken)));
            }

            foreach (string currency in _settings.Currencies)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, currency, cancellationToken)));
            }

            if (_settings.ConversionDirection == 1)
            {
                conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, _settings.LocalCurrency, cancellationToken)));
            }
        }
        else
        {
            conversionTasks.Add((index++, GetConversionAsync(amountToConvert, fromCurrency, toCurrency, cancellationToken)));
        }

        await Task.WhenAll(conversionTasks.Select(t => t.task)).ConfigureAwait(false);

        var results = new ListItem?[conversionTasks.Count];
        foreach (var task in conversionTasks)
        {
            results[task.index] = await task.task.ConfigureAwait(false);
        }

        return results.Where(r => r != null).Select(r => r!).ToList();
    }

    private async Task<ListItem?> GetConversionAsync(
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
            decimal conversionRate = await GetConversionRateAsync(fromCurrency, toCurrency, cancellationToken).ConfigureAwait(false);
            (decimal convertedAmount, int precision) = CalculateConvertedAmount(amountToConvert, conversionRate);

            string fromFormatted = amountToConvert.ToString("N", CultureInfo.CurrentCulture);
            string toFormatted = (amountToConvert < 0 ? convertedAmount * -1 : convertedAmount).ToString($"N{precision}", CultureInfo.CurrentCulture);

            string compressedOutput = $"{toFormatted} {toCurrency.ToUpperInvariant()}";
            string expandedOutput = $"{fromFormatted} {fromCurrency.ToUpperInvariant()} = {toFormatted} {toCurrency.ToUpperInvariant()}";

            return new ListItem(new CopyTextCommand(toFormatted)
            {
                Result = CommandResult.ShowToast(new ToastArgs()
                {
                    Message = "Copied to clipboard",
                    Result = CommandResult.Hide()
                })
            })
            {
                Title = _settings.OutputStyle == 0 ? compressedOutput : expandedOutput,
                Subtitle = $"Currency conversion from {fromCurrency.ToUpperInvariant()} to {toCurrency.ToUpperInvariant()}",
                Icon = IconManager.Icon,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return CreateErrorItem("Unable to reach the conversion service", "Press enter or click to open the currencies list");
        }
        catch (JsonException)
        {
            return CreateErrorItem("Received an invalid response from the conversion service", "Press enter or click to open the currencies list");
        }
        catch (InvalidOperationException e)
        {
            return CreateErrorItem(e.Message, "Press enter or click to open the currencies list");
        }
        catch (Exception)
        {
            return CreateErrorItem("Something went wrong while converting currencies", "Press enter or click to open the currencies list");
        }
    }

    private ListItem CreateErrorItem(string title, string subtitle) =>
        new(new OpenUrlCommand(_converterSettings.GetHelperLink()))
        {
            Title = title,
            Subtitle = subtitle,
            Icon = IconManager.WarningIcon,
        };

    private async Task<decimal> GetConversionRateAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken)
    {
        var cacheKey = (fromCurrency, toCurrency);

        if (_conversionCache.TryGetValue(cacheKey, out var directCacheData) &&
            directCacheData.Timestamp > DateTime.UtcNow.AddHours(-_settings.ConversionCacheDuration))
        {
            return directCacheData.Rate;
        }

        string url = _converterSettings.GetConversionLink(fromCurrency, toCurrency);
        using HttpResponseMessage response = await GetWithFallbackAsync(url, fromCurrency, toCurrency, cancellationToken).ConfigureAwait(false);

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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

        return cacheOutput.Rate;
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

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new InvalidOperationException($"{fromCurrency.ToUpperInvariant()} is not a valid currency");
        }

        response.Dispose();

        string fallbackUrl = _converterSettings.GetConversionFallbackLink(fromCurrency, toCurrency);
        HttpResponseMessage fallbackResponse = await _httpClient.GetAsync(fallbackUrl, cancellationToken).ConfigureAwait(false);

        if (fallbackResponse.IsSuccessStatusCode)
        {
            return fallbackResponse;
        }

        var statusCode = fallbackResponse.StatusCode;
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
    }
}
