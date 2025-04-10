using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Properties;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CurrencyConverterExtension.Helpers
{
    public class SettingsManager : JsonSettingsManager
    {
        private static readonly string _namespace = "currency-converter";
        private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

        private readonly ChoiceSetSetting _outputStyle = new(
            Namespaced(nameof(OutputStyle)),
            Resources.output_style,
            Resources.output_style_description,
            new()
            {
                new(Resources.output_style_short_text, "0"),
                new(Resources.output_style_full_text, "1"),
            })
        { Value = "1" };

        private readonly ChoiceSetSetting _decimalSeparator = new(
            Namespaced(nameof(DecimalSeparator)),
            Resources.decimal_separator,
            Resources.decimal_separator_description,
            new()
            {
                new(Resources.use_system_default, "0"),
                new(Resources.use_dots, "1"),
                new(Resources.use_commas, "2"),
            })
        { Value = "0" };

        private readonly ChoiceSetSetting _conversionDirection = new(
            Namespaced(nameof(ConversionDirection)),
            Resources.conversion_direction,
            Resources.conversion_direction_description,
            new()
            {
                new(Resources.local_to_other, "0"),
                new(Resources.other_to_local, "1"),
            })
        { Value = "0" };

        private readonly TextSetting _localCurrency = new(
            Namespaced(nameof(LocalCurrency)),
            Resources.local_currency,
            Resources.local_currency_description,
            new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol);

        private readonly TextSetting _currencies = new(
            Namespaced(nameof(Currencies)),
            Resources.currencies,
            Resources.currencies_description,
            "USD");

        private readonly TextSetting _conversionCacheDuration = new(
            Namespaced(nameof(ConversionCacheDuration)),
            Resources.cache_duration,
            Resources.cache_duration_description,
            "3");

        private readonly ChoiceSetSetting _conversionAPI = new(
            Namespaced(nameof(ConversionAPI)),
            Resources.conversion_api,
            Resources.conversion_api_description,
            new()
            {
                new(Resources.default_api, ((int)ConverterSettingsEnum.Default).ToString()),
                new(Resources.exchange_rate_api, ((int)ConverterSettingsEnum.ExchangeRateAPI).ToString()),
                new(Resources.currency_api, ((int)ConverterSettingsEnum.CurrencyAPI).ToString()),
            })
        { Value = ((int)ConverterSettingsEnum.Default).ToString() };

        private readonly TextSetting _conversionAPIKey = new(
            Namespaced(nameof(ConversionAPIKey)),
            Resources.api_key,
            Resources.api_key_description,
            "");

        public int OutputStyle => int.Parse(_outputStyle.Value);
        public int DecimalSeparator => int.Parse(_decimalSeparator.Value);
        public int ConversionDirection => int.Parse(_conversionDirection.Value);
        public string LocalCurrency => _localCurrency.Value;
        public string[] Currencies => _currencies.Value.Split(',').Select(x => x.Trim()).ToArray();
        public double ConversionCacheDuration => Math.Min(Math.Max(double.Parse(_conversionCacheDuration.Value), 0.5), 24);
        public int ConversionAPI => int.Parse(_conversionAPI.Value);
        public string ConversionAPIKey => _conversionAPIKey.Value;

        internal static string SettingsJsonPath()
        {
            var dir = Utilities.BaseSettingsPath("Microsoft.CmdPal");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "currency-converter.json");
        }

        public SettingsManager()
        {
            FilePath = SettingsJsonPath();
            Settings.Add(_outputStyle);
            Settings.Add(_decimalSeparator);
            Settings.Add(_conversionDirection);
            Settings.Add(_localCurrency);
            Settings.Add(_currencies);
            Settings.Add(_conversionCacheDuration);
            Settings.Add(_conversionAPI);
            Settings.Add(_conversionAPIKey);
            // Load settings from file upon initialization
            LoadSettings();
            Settings.SettingsChanged += (s, a) => this.SaveSettings();
        }
    }
}