using CurrencyConverterExtension.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CurrencyConverterExtension.Converter;

public class ConverterSettings
{
    public string ConversionDate { get; set; } = "latest";

    private IConversionSettings _settings { get; }

    public ConverterSettings(IConversionSettings settings)
    {
        _settings = settings;
    }

    private readonly Dictionary<string, Dictionary<string, string>> _options = new()
    {
        {
            "Default", new()
            {
                {"ConversionLink", "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{date}/v1/currencies/{from}.min.json"},
                {"ConversionFallbackLink", "https://{date}.currency-api.pages.dev/v1/currencies/{from}.min.json"},
                {"ConversionHelperLink", "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies.json"},
            }
        },
        {
            "ExchangeRateAPI", new()
            {
                {"ConversionLink", "https://v6.exchangerate-api.com/v6/{api_key}/{date}/{from}"},
                {"ConversionFallbackLink", "https://v6.exchangerate-api.com/v6/{api_key}/{date}/{from}"},
                {"ConversionHelperLink", "https://www.exchangerate-api.com/docs/supported-currencies"},
            }
        },
        {
            "CurrencyAPI", new()
            {
                {"ConversionLink", "https://api.currencyapi.com/v3/{date}?apikey={api_key}&base_currency={from}"},
                {"ConversionFallbackLink", "https://api.currencyapi.com/v3/{date}?apikey={api_key}&base_currency={from}"},
                {"ConversionHelperLink", "https://currencyapi.com/docs/currency-list"},
            }
        }
    };

    private string ParseLink(string link, string from, string to) => link
        .Replace("{api_key}", _settings.ConversionAPIKey)
        .Replace("{date}", ConversionDate)
        .Replace("{from}", _settings.ConversionAPI == (int)ConverterSettingsApi.CurrencyAPI ? from.ToUpperInvariant() : from)
        .Replace("{to}", to);
    private Dictionary<string, string> GetOption()
    {
        if (!Enum.IsDefined(typeof(ConverterSettingsApi), _settings.ConversionAPI))
        {
            return _options[nameof(ConverterSettingsApi.Default)];
        }

        string key = ((ConverterSettingsApi)_settings.ConversionAPI).ToString();
        return _options.TryGetValue(key, out Dictionary<string, string>? option)
            ? option
            : _options[nameof(ConverterSettingsApi.Default)];
    }

    internal string GetConversionLink(string from, string to) => ParseLink(GetOption()["ConversionLink"], from, to);
    internal string GetConversionFallbackLink(string from, string to) => ParseLink(GetOption()["ConversionFallbackLink"], from, to);
    internal string GetHelperLink() => GetOption()["ConversionHelperLink"];

    private void EnsureConversionAPIKey()
    {
        if (string.IsNullOrEmpty(_settings.ConversionAPIKey))
            throw new InvalidOperationException("Conversion API Key is not provided");
    }

    internal void ValidateConversionAPI()
    {
        if (!Enum.IsDefined(typeof(ConverterSettingsApi), _settings.ConversionAPI))
        {
            throw new InvalidOperationException("Invalid Conversion API selected. Open settings and choose a valid API.");
        }

        if (_settings.ConversionAPI != (int)ConverterSettingsApi.Default)
            EnsureConversionAPIKey();
    }

    internal JsonElement GetRootJsonElementFor(string content, string fromCurrency)
    {
        using JsonDocument doc = JsonDocument.Parse(content);
        JsonElement root = doc.RootElement;

        JsonElement GetProperty(string property) => root.GetProperty(property).Clone();

        switch (_settings.ConversionAPI)
        {
            case (int)ConverterSettingsApi.Default: return GetProperty(fromCurrency);
            case (int)ConverterSettingsApi.ExchangeRateAPI: return GetProperty("conversion_rates");
            case (int)ConverterSettingsApi.CurrencyAPI: return GetProperty("data");
        }

        throw new InvalidOperationException("Invalid Conversion API selected.");
    }

    internal (string, decimal) GetRateFor(JsonProperty property)
    {
        if (_settings.ConversionAPI == (int)ConverterSettingsApi.CurrencyAPI)
        {
            if (property.Value.TryGetProperty("code", out JsonElement codeElement))
            {
                string? code = codeElement.GetString();
                if (!string.IsNullOrEmpty(code) && property.Value.TryGetProperty("value", out JsonElement valueElement))
                {
                    decimal value = valueElement.GetDecimal();
                    return (code, value);
                }
            }
            throw new InvalidOperationException("Invalid JSON structure: missing 'code' or 'value'.");
        }

        return (property.Name, property.Value.GetDecimal());
    }

}

public enum ConverterSettingsApi
{
    Default,
    ExchangeRateAPI,
    CurrencyAPI,
}
