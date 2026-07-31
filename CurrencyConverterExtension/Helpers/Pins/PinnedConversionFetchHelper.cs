using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CurrencyConverterExtension.Helpers;

/// <summary>
/// Fetches pinned conversions in parallel across distinct from-currencies,
/// but sequentially within each from-currency so the first response can warm
/// the shared rate cache for sibling pins.
/// </summary>
internal static class PinnedConversionFetchHelper
{
    internal static async Task<T[]> FetchGroupedByFromCurrencyAsync<T>(
        IReadOnlyList<PinnedConversion> pins,
        Func<PinnedConversion, Task<T>> fetchAsync)
    {
        if (pins.Count == 0)
        {
            return [];
        }

        T[] results = new T[pins.Count];

        IEnumerable<IGrouping<string, (int Index, PinnedConversion Pin)>> groups = pins
            .Select((pin, index) => (Index: index, Pin: pin))
            .GroupBy(x => x.Pin.FromCurrency, StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(groups.Select(async group =>
        {
            foreach ((int index, PinnedConversion pin) in group)
            {
                results[index] = await fetchAsync(pin).ConfigureAwait(false);
            }
        })).ConfigureAwait(false);

        return results;
    }
}