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
        string local = Resolve(localCurrency, aliases);
        string firstGlobal = currencies.Count > 0 ? Resolve(currencies[0], aliases) : string.Empty;
        string from = Resolve(query.FromCurrency, aliases);
        string to = Resolve(query.ToCurrency, aliases);

        string selectedFrom;
        string selectedTo;

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

    private static string Resolve(string? currency, AliasManager aliases)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return string.Empty;
        }

        string lowered = currency.Trim().ToLowerInvariant();
        if (aliases.HasAlias(lowered))
        {
            string? mapped = aliases.GetAlias(lowered);
            if (!string.IsNullOrEmpty(mapped))
            {
                return mapped.ToLowerInvariant();
            }
        }

        return lowered;
    }

    private static bool CurrenciesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
