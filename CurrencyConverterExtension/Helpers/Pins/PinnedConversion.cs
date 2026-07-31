using System;
using System.Globalization;

namespace CurrencyConverterExtension.Helpers;

internal readonly record struct PinnedConversion(decimal Amount, string FromCurrency, string ToCurrency)
{
    public bool Equals(PinnedConversion other) =>
        Amount == other.Amount
        && string.Equals(FromCurrency, other.FromCurrency, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ToCurrency, other.ToCurrency, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        Amount,
        StringComparer.OrdinalIgnoreCase.GetHashCode(FromCurrency),
        StringComparer.OrdinalIgnoreCase.GetHashCode(ToCurrency));

    internal string ToDisplayLabel()
    {
        string from = FromCurrency.ToUpperInvariant();
        string to = ToCurrency.ToUpperInvariant();
        string amount = Amount.ToString("N", CultureInfo.CurrentCulture);
        return $"{amount} {from} → {to}";
    }
}
