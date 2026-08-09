// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CurrencyConverterExtension.Converter;
using CurrencyConverterExtension.Helpers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CurrencyConverterExtension;

internal sealed partial class CurrencyConverterTodaysRatesPage : DynamicListPage, IDisposable
{
    internal readonly SettingsManager _settings;
    internal readonly CurrencyConverter _converter;
    internal readonly AliasManager _aliasManager;
    internal readonly PinnedConversionManager _pinManager;

    private readonly ConversionSearchController _search;
    private IListItem[] _items = [];
    private CancellationTokenSource? _defaultCts;
    private int _defaultLoadInFlight;
    private int _defaultLoadVersion;
    private int _suppressSearchUpdate;
    private int _defaultViewLoaded;

    /// <summary>Raised after the default rates view finishes loading (pins + quick rates).</summary>
    public event Action? RatesRefreshed;

    public CurrencyConverterTodaysRatesPage(
        SettingsManager settings,
        AliasManager aliasManager,
        PinnedConversionManager pinManager,
        CurrencyConverter converter)
    {
        Id = "CurrencyConverterTodaysRatesPage";
        Icon = IconManager.Icon;
        Title = "Today's rates";
        Name = "Today's rates";
        ShowDetails = true;
        EmptyContent = new ListItem(new NoOpCommand())
        {
            Title = "No conversion results",
            Subtitle = "Try a query like 34 BTC to AED, then press Enter to pin it",
            Icon = IconManager.Icon,
        };

        _settings = settings;
        _aliasManager = aliasManager;
        _pinManager = pinManager;
        _converter = converter;

        _search = new ConversionSearchController(
            settings,
            aliasManager,
            pinManager,
            converter,
            BuildSearchResultItemsAsync,
            items => _items = items,
            loading => IsLoading = loading,
            RaiseItemsChanged);

        // Keep default view in sync when pins change from the main converter (or elsewhere).
        _pinManager.PinsChanged += OnPinsChangedExternally;
    }

    private void SetSearchTextWithoutConverting(string query)
    {
        Interlocked.Exchange(ref _suppressSearchUpdate, 1);
        try
        {
            SearchText = query;
            OnPropertyChanged(nameof(SearchText));
        }
        finally
        {
            Interlocked.Exchange(ref _suppressSearchUpdate, 0);
        }
    }

    private void OnPinsChangedExternally()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            _ = LoadDefaultViewAsync();
        }
    }

    public override IListItem[] GetItems()
    {
        if (SearchText.Length == 0)
        {
            if (_defaultViewLoaded == 0 && _defaultLoadInFlight == 0)
            {
                _ = LoadDefaultViewAsync();
            }

            return _items.Length == 0 ? [CreateLoadingItem()] : _items;
        }

        return _items;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (_suppressSearchUpdate != 0)
        {
            return;
        }

        if (oldSearch == newSearch)
        {
            return;
        }

        if (string.IsNullOrEmpty(newSearch))
        {
            _ = LoadDefaultViewAsync();
            return;
        }

        IsLoading = true;
        _ = DebounceAndConvertFromSearchAsync(newSearch);
    }

    private void CancelSearchWork() => _search.CancelPendingWork();

    private async Task LoadDefaultViewAsync()
    {
        CancelSearchWork();

        CancellationTokenSource defaultCts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _defaultCts, defaultCts);
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken ct = defaultCts.Token;
        int version = Interlocked.Increment(ref _defaultLoadVersion);

        Interlocked.Exchange(ref _defaultLoadInFlight, 1);
        Interlocked.Exchange(ref _defaultViewLoaded, 0);
        _items = [CreateLoadingItem()];
        IsLoading = true;
        RaiseItemsChanged(0);

        try
        {
            await _aliasManager.EnsureInitializedAsync().ConfigureAwait(false);
            await _pinManager.EnsureInitializedAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            try
            {
                _converter.ValidateConversionAPI();
            }
            catch (Exception ex)
            {
                List<IListItem> errorItems = [];
                if (_pinManager.GetAllPins().Count == 0)
                {
                    errorItems.Add(CreateHintItem());
                }

                errorItems.Add(new ListItem(new OpenUrlCommand(CurrencyConverterExtensionPage.GithubReadmeURL))
                {
                    Title = ex.Message,
                    Subtitle = "Press enter or click to see how to fix this issue",
                    Icon = IconManager.WarningIcon,
                });
                _items = [.. errorItems];
                return;
            }

            List<IListItem> items = [];
            List<PinnedConversion> pins = _pinManager.GetAllPins();

            string local = _settings.LocalCurrency.Trim();
            string[] otherCurrencies = [.. _settings.Currencies
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            string[] convertible = [.. otherCurrencies
                .Where(c => !string.Equals(c, local, StringComparison.OrdinalIgnoreCase))];
            bool hasConvertibleOther = convertible.Length > 0;

            ct.ThrowIfCancellationRequested();

            // Kick off pin and quick-rate fetches together so different bases overlap.
            Task<List<ConversionOutcome>[]> pinsTask = pins.Count == 0
                ? Task.FromResult(Array.Empty<List<ConversionOutcome>>())
                : Task.WhenAll(pins.Select(pin => _converter.GetConversionOutcomesAsync(
                    pin.Amount,
                    pin.FromCurrency,
                    pin.ToCurrency,
                    ct)));

            Task<List<ConversionOutcome>[]> ratesTask = !hasConvertibleOther
                ? Task.FromResult(Array.Empty<List<ConversionOutcome>>())
                : Task.WhenAll(convertible.Select(currency => _converter.GetConversionOutcomesAsync(
                    1m,
                    local,
                    currency,
                    ct)));

            await Task.WhenAll(pinsTask, ratesTask).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (pins.Count == 0)
            {
                items.Add(CreateHintItem());
            }
            else
            {
                List<ConversionOutcome>[] outcomesByPin = await pinsTask.ConfigureAwait(false);
                for (int i = 0; i < pins.Count; i++)
                {
                    PinnedConversion pin = pins[i];
                    List<ConversionOutcome> outcomes = outcomesByPin[i];

                    if (outcomes.Count == 0)
                    {
                        items.Add(ConversionResultItemFactory.CreatePinnedPlaceholder(pin, _pinManager, OnPinned));
                        continue;
                    }

                    foreach (ConversionOutcome outcome in outcomes)
                    {
                        items.Add(ConversionResultItemFactory.Create(
                            outcome,
                            _pinManager,
                            OnPinned,
                            treatAsPinned: true));
                    }
                }
            }

            if (!hasConvertibleOther)
            {
                items.Add(new ListItem(_settings.Settings.SettingsPage)
                {
                    Title = "No quick rates available",
                    Subtitle = "Local currency matches your other currencies. Press Enter to open Settings and add a different currency.",
                    Icon = IconManager.WarningIcon,
                });
            }
            else
            {
                List<ConversionOutcome>[] outcomesByCurrency = await ratesTask.ConfigureAwait(false);
                foreach (List<ConversionOutcome> outcomes in outcomesByCurrency)
                {
                    foreach (ConversionOutcome outcome in outcomes)
                    {
                        items.Add(outcome.Item);
                    }
                }
            }

            _items = [.. items];
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            List<IListItem> errorItems = [];
            if (_pinManager.IsInitialized && _pinManager.GetAllPins().Count == 0)
            {
                errorItems.Add(CreateHintItem());
            }

            errorItems.Add(new ListItem(new NoOpCommand())
            {
                Title = "Something went wrong while loading today's rates",
                Subtitle = "Please try again",
                Icon = IconManager.WarningIcon,
            });
            _items = [.. errorItems];
        }
        finally
        {
            // Always clear loading for the newest load so a cancel chain cannot leave a stuck spinner.
            if (version == _defaultLoadVersion)
            {
                Interlocked.Exchange(ref _defaultLoadInFlight, 0);
                if (!ct.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _defaultViewLoaded, 1);
                    IsLoading = false;
                    RaiseItemsChanged(0);
                    RatesRefreshed?.Invoke();
                }
                else
                {
                    IsLoading = false;
                }
            }
        }
    }

    private ListItem CreateLoadingItem() =>
        new(new NoOpCommand())
        {
            Title = "Loading today's rates…",
            Subtitle = "Please wait",
            Icon = Icon,
        };

    private async Task DebounceAndConvertFromSearchAsync(string search)
    {
        CancellationTokenSource? previousDefault = Interlocked.Exchange(ref _defaultCts, null);
        previousDefault?.Cancel();
        previousDefault?.Dispose();
        Interlocked.Exchange(ref _defaultLoadInFlight, 0);

        await _search.DebounceAndConvertAsync(search).ConfigureAwait(false);
    }

    private async Task<IListItem[]> BuildSearchResultItemsAsync(ParsedQuery query, CancellationToken cancellationToken)
    {
        List<ConversionOutcome> outcomes = await _converter.GetConversionOutcomesAsync(
            query.Amount,
            query.FromCurrency,
            query.ToCurrency,
            cancellationToken).ConfigureAwait(false);

        return [.. outcomes
            .GroupBy(o => new { o.Item.Title, o.Item.Subtitle })
            .Select(g => g.First())
            .Select(o => ConversionResultItemFactory.Create(o, _pinManager, OnPinned, ConversionPinAction.Primary))];
    }

    private static ListItem CreateHintItem() =>
        new(new NoOpCommand())
        {
            Title = "Pin custom conversions",
            Subtitle = "Search a conversion like 34 BTC to AED, then press Enter to pin it here",
            Icon = IconManager.Icon,
        };

    private void OnPinned()
    {
        // When search is already empty, PinsChanged → OnPinsChangedExternally reloads.
        // When pinning from search, PinsChanged fires while search is non-empty (skipped),
        // so clear search and reload here.
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        SetSearchTextWithoutConverting(string.Empty);
        _ = LoadDefaultViewAsync();
    }

    public void Dispose()
    {
        _pinManager.PinsChanged -= OnPinsChangedExternally;
        _search.Dispose();
        _defaultCts?.Cancel();
        _defaultCts?.Dispose();
    }
}
