namespace CurrencyConverterExtension.Helpers;

public interface IConversionSettings
{
    int DecimalSeparator { get; }
    string LocalCurrency { get; }
    string[] Currencies { get; }
    double ConversionCacheDuration { get; }
    int ConversionAPI { get; }
    string ConversionAPIKey { get; }
}