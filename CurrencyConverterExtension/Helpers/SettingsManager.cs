using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Properties;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CurrencyConverterExtension.Helpers
{
    public class SettingsManager : JsonSettingsManager, IConversionSettings
    {
        private static readonly string _namespace = "currency-converter";
        private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

        // CmdPal often only renders Description; include the title so it stays visible.
        private static string WithTitle(string title, string description) =>
            string.IsNullOrEmpty(description) ? title : $"{title}\n{description}";

        private readonly ChoiceSetSetting _decimalSeparator = new(
            Namespaced(nameof(DecimalSeparator)),
            Resources.decimal_separator,
            WithTitle(Resources.decimal_separator, Resources.decimal_separator_description),
            new()
            {
                new(Resources.use_system_default, "0"),
                new(Resources.use_dots, "1"),
                new(Resources.use_commas, "2"),
            })
        { Value = "0" };

        private readonly TextSetting _localCurrency = new(
            Namespaced(nameof(LocalCurrency)),
            Resources.local_currency,
            WithTitle(Resources.local_currency, Resources.local_currency_description),
            new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol);

        private readonly TextSetting _currencies = new(
            Namespaced(nameof(Currencies)),
            Resources.currencies,
            WithTitle(Resources.currencies, Resources.currencies_description),
            "USD");

        private readonly TextSetting _conversionCacheDuration = new(
            Namespaced(nameof(ConversionCacheDuration)),
            Resources.cache_duration,
            WithTitle(Resources.cache_duration, Resources.cache_duration_description),
            "3");

        private readonly ChoiceSetSetting _conversionAPI = new(
            Namespaced(nameof(ConversionAPI)),
            Resources.conversion_api,
            WithTitle(Resources.conversion_api, Resources.conversion_api_description),
            new()
            {
                new(Resources.default_api, ((int)ConverterSettingsApi.Default).ToString(CultureInfo.InvariantCulture)),
                new(Resources.exchange_rate_api, ((int)ConverterSettingsApi.ExchangeRateAPI).ToString(CultureInfo.InvariantCulture)),
                new(Resources.currency_api, ((int)ConverterSettingsApi.CurrencyAPI).ToString(CultureInfo.InvariantCulture)),
            })
        { Value = ((int)ConverterSettingsApi.Default).ToString(CultureInfo.InvariantCulture) };

        private readonly TextSetting _conversionAPIKey = new(
            Namespaced(nameof(ConversionAPIKey)),
            Resources.api_key,
            WithTitle(Resources.api_key, Resources.api_key_description),
            "");

        public int DecimalSeparator => int.TryParse(_decimalSeparator.Value, out int decimalSeparator) ? decimalSeparator : 0;
        public string LocalCurrency => string.IsNullOrWhiteSpace(_localCurrency.Value)
            ? new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol
            : _localCurrency.Value.Trim();
        public string[] Currencies => string.IsNullOrWhiteSpace(_currencies.Value)
            ? ["USD"]
            : [.. _currencies.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)];
        public double ConversionCacheDuration
        {
            get
            {
                double duration = double.TryParse(_conversionCacheDuration.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : 3;
                return Math.Min(Math.Max(duration, 0.5), 24);
            }
        }
        public int ConversionAPI => int.TryParse(_conversionAPI.Value, out int conversionApi) ? conversionApi : 0;
        public string ConversionAPIKey => _conversionAPIKey.Value ?? string.Empty;

        internal static string SettingsJsonPath()
        {
            var dir = Utilities.BaseSettingsPath("Microsoft.CmdPal");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public SettingsManager()
        {
            FilePath = SettingsJsonPath();
            Settings.Add(_localCurrency);
            Settings.Add(_currencies);
            Settings.Add(_decimalSeparator);
            Settings.Add(_conversionCacheDuration);
            Settings.Add(_conversionAPI);
            Settings.Add(_conversionAPIKey);
            // Load settings from file upon initialization
            LoadSettings();
            Settings.SettingsChanged += (s, a) => this.SaveSettings();
        }
    }
}
