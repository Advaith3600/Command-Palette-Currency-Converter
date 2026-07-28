namespace CurrencyConverterExtension.Helpers;

public interface IConversionSettings
{
    int OutputStyle { get; }
    int DecimalSeparator { get; }
    int ConversionDirection { get; }
    string LocalCurrency { get; }
    string[] Currencies { get; }
    double ConversionCacheDuration { get; }
    int ConversionAPI { get; }
    string ConversionAPIKey { get; }
}