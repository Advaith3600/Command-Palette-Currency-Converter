using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;

namespace CurrencyConverterExtension.Tests.Fakes;

internal sealed class FakeConversionSettings : IConversionSettings
{
    public int OutputStyle { get; set; } = 1;
    public int DecimalSeparator { get; set; } = 1;
    public int ConversionDirection { get; set; }
    public string LocalCurrency { get; set; } = "usd";
    public string[] Currencies { get; set; } = ["eur", "inr"];
    public double ConversionCacheDuration { get; set; } = 3;
    public int ConversionAPI { get; set; } = (int)ConverterSettingsApi.Default;
    public string ConversionAPIKey { get; set; } = "";
}