using CurrencyConverterExtension.Helpers;
using System;
using System.Collections.Generic;

namespace CurrencyConverterExtension.Converter;

internal readonly record struct FallbackConversionPair(string FromCurrency, string ToCurrency);

/// <summary>
/// Picks the single from/to pair shown on the Command Palette home-list fallback.
/// </summary>
internal static class FallbackConversionSelector
{
    internal static FallbackConversionPair? TrySelect(
        ParsedQuery query,
        string localCurrency,
        IReadOnlyList<string> currencies,
        AliasManager aliases)
    {
        string local = CurrencyNames.Resolve(localCurrency, aliases, localCurrency);
        string firstGlobal = currencies.Count > 0 ? CurrencyNames.Resolve(currencies[0], aliases, localCurrency) : string.Empty;
        string from = CurrencyNames.Resolve(query.FromCurrency, aliases, localCurrency);
        string to = CurrencyNames.Resolve(query.ToCurrency, aliases, localCurrency);

        string selectedFrom;
        string selectedTo;

        if (query.IsMixed)
        {
            // A mixed total is meaningful in any currency, including one of its parts.
            selectedTo = !string.IsNullOrEmpty(to) ? to
                : !string.IsNullOrEmpty(local) ? local
                : firstGlobal;
            return string.IsNullOrEmpty(selectedTo) ? null : new FallbackConversionPair(from, selectedTo);
        }

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
        {
            selectedFrom = from;
            selectedTo = to;
        }
        else if (!string.IsNullOrEmpty(from))
        {
            selectedFrom = from;
            selectedTo = CurrenciesEqual(from, local) ? firstGlobal : local;
        }
        else if (!string.IsNullOrEmpty(to))
        {
            selectedFrom = local;
            selectedTo = to;
        }
        else
        {
            selectedFrom = local;
            selectedTo = firstGlobal;
        }

        if (string.IsNullOrEmpty(selectedFrom)
            || string.IsNullOrEmpty(selectedTo)
            || CurrenciesEqual(selectedFrom, selectedTo))
        {
            return null;
        }

        return new FallbackConversionPair(selectedFrom, selectedTo);
    }

    private static bool CurrenciesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
